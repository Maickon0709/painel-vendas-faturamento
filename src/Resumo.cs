// =====================================================================
//  Resumo.cs  -  Interpreta o "Resumo por Vendedor [Pedido]"
//
//  Recebe as linhas de texto do relatório (do PDF ou de um .txt/.csv)
//  e descobre o que é cada linha:
//    - cabeçalho da página (empresa, título, data, página, seleção)  -> ignora
//    - títulos de coluna                                             -> ignora
//    - linha de VENDEDOR (código + nome)
//    - linha de PEDIDO   (nº, data, cliente, quantidade, valor)
//    - TOTAL DO VENDEDOR (só números, ou "Total ...")
//    - TOTAL GERAL
//  O que não se encaixa em nada vai para "linhas não reconhecidas",
//  que aparecem na tela de Conferência.
// =====================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PainelPedidos
{
    public class Pedido
    {
        public string Numero, CodCliente, Cliente;
        public string Empresa = "";      // nome curto da empresa do PDF (ex.: BETA)
        public DateTime? Data;
        public double Quantidade, Valor;
        public Vendedor Vendedor;
        public bool Licitacao;            // venda passada para outro vendedor por ser licitação
        public string VendedorOriginal;   // vendedor que estava no PDF (quando é licitação)
        public string ChaveCliente { get { return string.IsNullOrEmpty(CodCliente) ? Resumo.Normalizar(Cliente) : CodCliente; } }
        // Com várias empresas o código do cliente pode mudar de um sistema para outro: junta pelo nome
        public string Chave(bool variasEmpresas) { return variasEmpresas ? Resumo.Normalizar(Cliente) : ChaveCliente; }
    }

    public class Vendedor
    {
        public string Codigo, Nome;
        public List<Pedido> Pedidos = new List<Pedido>();
        public double? TotalInformado;       // o que o relatório diz
        public double? QuantidadeInformada;
        public bool Manual;                 // vendedor criado no programa (não está no PDF)
        public double TotalLido;            // soma dos pedidos como vieram no PDF (para a conferência)
        public double Transferido;          // quanto saiu dele para licitação
        public double Total { get { return Pedidos.Sum(p => p.Valor); } }
        public double Quantidade { get { return Pedidos.Sum(p => p.Quantidade); } }
        public string Rotulo { get { return string.IsNullOrEmpty(Codigo) ? Nome : Codigo + " - " + Nome; } }
    }

    public class ClienteResumo
    {
        public string Codigo, Nome, Chave, Empresas;
        public bool VariasEmpresas;
        public int Pedidos;
        public double Quantidade, Valor;
        public DateTime? Ultima;
    }

    public class Resumo
    {
        public string Empresa = "", Periodo = "", Emissao = "", Arquivo = "";
        public bool EhFaturamento;   // "Vendas por Vendedor - Sobre Faturamento" (senão: Resumo por Vendedor [Pedido])
        public string EmpresaCurta = "";
        public List<Resumo> Partes = new List<Resumo>();   // quando é a soma de vários PDFs
        public List<Vendedor> Zerados = new List<Vendedor>(); // vendedores do PDF que ficaram sem pedidos (tudo foi p/ licitação)

        // Cópia independente (as regras de licitação mexem na cópia, não no PDF lido)
        public Resumo Clonar()
        {
            var r = (Resumo)MemberwiseClone();
            Func<Vendedor, Vendedor> cv = v =>
            {
                var n = new Vendedor { Codigo = v.Codigo, Nome = v.Nome, TotalInformado = v.TotalInformado, QuantidadeInformada = v.QuantidadeInformada,
                                       Manual = v.Manual, TotalLido = v.TotalLido, Transferido = v.Transferido };
                foreach (var p in v.Pedidos)
                    n.Pedidos.Add(new Pedido { Numero = p.Numero, CodCliente = p.CodCliente, Cliente = p.Cliente, Empresa = p.Empresa, Data = p.Data,
                                               Quantidade = p.Quantidade, Valor = p.Valor, Vendedor = n, Licitacao = p.Licitacao, VendedorOriginal = p.VendedorOriginal });
                return n;
            };
            r.Vendedores = Vendedores.Select(cv).ToList();
            r.SemVendedor = SemVendedor.Select(cv).ToList();
            r.Zerados = Zerados.Select(cv).ToList();
            return r;
        }
        public List<Vendedor> Vendedores = new List<Vendedor>();
        public double? TotalGeralInformado, QuantidadeGeralInformada;
        public List<string> NaoReconhecidas = new List<string>();
        public List<string> TextoLido = new List<string>();

        public IEnumerable<Pedido> Pedidos { get { return Vendedores.SelectMany(v => v.Pedidos); } }
        public double TotalGeral { get { return Vendedores.Sum(v => v.Total); } }

        // Vendas SEM VENDEDOR: ficam de fora dos totais e rankings, mas entram na conferência
        public List<Vendedor> SemVendedor = new List<Vendedor>();
        public double TotalSemVendedor { get { return SemVendedor.Sum(v => v.Total); } }
        public int PedidosSemVendedor { get { return SemVendedor.Sum(v => v.Pedidos.Count); } }

        public List<ClienteResumo> Clientes(Vendedor somente)
        {
            var fonte = (somente == null ? Pedidos : somente.Pedidos).ToList();
            bool varias = fonte.Select(p => p.Empresa).Distinct().Count() > 1;
            return fonte.GroupBy(p => p.Chave(varias)).Select(g => new ClienteResumo
            {
                Chave = g.Key,
                VariasEmpresas = varias,
                Empresas = string.Join(", ", g.Select(p => p.Empresa).Distinct().OrderBy(x => x)),
                Codigo = varias ? string.Join(" / ", g.Select(p => p.CodCliente).Where(c => !string.IsNullOrEmpty(c)).Distinct()) : g.First().CodCliente,
                Nome = g.GroupBy(p => p.Cliente).OrderByDescending(x => x.Count()).First().Key,
                Pedidos = g.Count(),
                Quantidade = g.Sum(p => p.Quantidade),
                Valor = g.Sum(p => p.Valor),
                Ultima = g.Max(p => p.Data)
            }).OrderByDescending(c => c.Valor).ToList();
        }

        // ==============================================================
        //  Carregar
        // ==============================================================
        public static Resumo Carregar(string caminho)
        {
            byte[] bytes = LerComRetentativa(caminho);
            List<LinhaPdf> linhas;
            string ext = Path.GetExtension(caminho).ToLowerInvariant();
            if (ext == ".pdf" || (bytes.Length > 4 && Encoding.ASCII.GetString(bytes, 0, 4) == "%PDF"))
                linhas = PdfTexto.LerLinhas(bytes);
            else
                linhas = LinhasDeTexto(bytes);

            var r = Interpretar(linhas);
            r.Arquivo = caminho;
            r.EmpresaCurta = NomeCurto(r.Empresa, caminho);
            foreach (var p in r.Pedidos) p.Empresa = r.EmpresaCurta;
            return r;
        }

        // "BETA INDUSTRIA METALURGICA LTDA" -> "BETA"
        static string NomeCurto(string empresa, string caminho)
        {
            var palavras = Normalizar(empresa).Split(' ').Where(x => x.Length > 0).ToList();
            if (palavras.Count == 0) return Path.GetFileNameWithoutExtension(caminho).ToUpperInvariant();
            string nome = palavras[0];
            if ((nome.Length < 3 || nome.All(char.IsDigit)) && palavras.Count > 1) nome += " " + palavras[1];
            return nome;
        }

        // ==============================================================
        //  Soma de vários resumos (um PDF por empresa)
        //  Vendedor com o MESMO NOME em empresas diferentes vira um só.
        // ==============================================================
        public static Resumo Combinar(List<Resumo> lista)
        {
            if (lista.Count == 1) return lista[0];
            var r = new Resumo { Partes = lista };
            r.EmpresaCurta = "TODAS";
            r.Empresa = string.Join(" + ", lista.Select(x => x.EmpresaCurta));
            r.Periodo = string.Join(" / ", lista.Select(x => x.Periodo).Where(x => x != "").Distinct());
            r.Emissao = lista.Select(x => x.Emissao).Where(x => x != "").DefaultIfEmpty("").Max();

            var porNome = new Dictionary<string, Vendedor>();
            foreach (var parte in lista)
                foreach (var v in parte.Vendedores)
                {
                    string chave = Normalizar(v.Nome);
                    Vendedor junto;
                    if (!porNome.TryGetValue(chave, out junto))
                    {
                        junto = new Vendedor { Codigo = v.Codigo, Nome = v.Nome, TotalInformado = 0, QuantidadeInformada = 0, Manual = v.Manual };
                        porNome[chave] = junto;
                        r.Vendedores.Add(junto);
                    }
                    else if (junto.Codigo != v.Codigo) junto.Codigo = null; // códigos diferentes: mostra só o nome
                    junto.Manual = junto.Manual && v.Manual;
                    junto.TotalLido += v.TotalLido;
                    junto.Transferido += v.Transferido;
                    junto.Pedidos.AddRange(v.Pedidos);
                    junto.TotalInformado = junto.TotalInformado.HasValue && v.TotalInformado.HasValue ? junto.TotalInformado + v.TotalInformado : null;
                    junto.QuantidadeInformada = (junto.QuantidadeInformada ?? 0) + (v.QuantidadeInformada ?? 0);
                }
            r.TotalGeralInformado = lista.All(x => x.TotalGeralInformado.HasValue) ? lista.Sum(x => x.TotalGeralInformado.Value) : (double?)null;
            r.QuantidadeGeralInformada = lista.Sum(x => x.QuantidadeGeralInformada ?? 0);
            foreach (var parte in lista)
            {
                r.NaoReconhecidas.AddRange(parte.NaoReconhecidas.Select(x => parte.EmpresaCurta + "  " + x));
                r.SemVendedor.AddRange(parte.SemVendedor);
                r.Zerados.AddRange(parte.Zerados);
                r.TextoLido.Add("########## " + parte.EmpresaCurta + "  (" + Path.GetFileName(parte.Arquivo) + ") ##########");
                r.TextoLido.AddRange(parte.TextoLido);
                r.TextoLido.Add("");
            }
            return r;
        }

        static byte[] LerComRetentativa(string caminho)
        {
            for (int t = 0; ; t++)
            {
                try
                {
                    using (var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var ms = new MemoryStream()) { fs.CopyTo(ms); return ms.ToArray(); }
                }
                catch (IOException)
                {
                    if (t >= 4) throw;
                    System.Threading.Thread.Sleep(600 * (t + 1));
                }
            }
        }

        // .txt / .csv exportado: campos separados por TAB, ponto e vírgula ou 2+ espaços
        static List<LinhaPdf> LinhasDeTexto(byte[] bytes)
        {
            string texto;
            try { texto = new UTF8Encoding(false, true).GetString(bytes); }
            catch { texto = Encoding.GetEncoding(1252).GetString(bytes); }
            var r = new List<LinhaPdf>();
            int pagina = 1;
            foreach (var bruta in texto.Split('\n'))
            {
                string l = bruta.TrimEnd('\r');
                if (l.Contains("\f")) pagina++;
                l = l.Replace("\f", "");
                if (l.Trim().Length == 0) continue;
                string[] campos;
                if (l.Contains("\t")) campos = l.Split('\t');
                else if (l.Contains(";")) campos = l.Split(';');
                else campos = Regex.Split(l.Trim(), @"\s{2,}");
                var linha = new LinhaPdf { Pagina = pagina };
                foreach (var c in campos) { string s = c.Trim().Trim('"').Trim(); if (s.Length > 0) linha.Campos.Add(s); }
                if (linha.Campos.Count > 0) r.Add(linha);
            }
            return r;
        }

        // ==============================================================
        //  Classificação das linhas
        // ==============================================================
        enum Tipo { Indefinido, Cabecalho, Colunas, Pedido, Subtotal, TotalGeral, Texto }

        static readonly Regex RxData = new Regex(@"^\d{2}/\d{2}/\d{2,4}$");
        static readonly Regex RxMoeda = new Regex(@"^-?(R\$)?\s?\d{1,3}(\.\d{3})*,\d{2}$|^-?(R\$)?\s?\d+,\d{2}$");
        static readonly Regex RxQtd = new Regex(@"^-?\d{1,3}(\.\d{3})*,\d{3,4}$|^-?\d+,\d{3,4}$");
        static readonly Regex RxPct = new Regex(@"^-?\d+(,\d+)?%$|^%$");
        static readonly Regex RxCodigo = new Regex(@"^[A-Za-z]{0,3}\d{1,10}[A-Za-z]{0,2}$");
        static readonly string[] PALAVRAS_CABECALHO = { "RESUMO POR VENDEDOR", "VENDAS POR VENDEDOR", "SOBRE FATURAMENTO", "PAGINA", "SELECAO", "ORDEM", "EMISSAO" };
        static readonly HashSet<string> PALAVRAS_COLUNA = new HashSet<string> {
            "VENDEDOR","VENDEDORES","CODIGO","COD","COD.","NOME","VALOR","TOTAL","DATA","PEDIDO","PEDIDOS","CLIENTE","CLIENTES",
            "QUANTIDADE","QTD","QTDE","QUANT","NUMERO","N","NO","Nº","N°","DO","DA","DE","VENDA","VENDAS","EMISSAO","PESO","KG","UN","R$" };
        static readonly HashSet<string> PALAVRAS_SUBTOTAL = new HashSet<string> { "TOTAL", "SUBTOTAL", "SUB-TOTAL", "DO", "DA", "VENDEDOR", "R$", ":", "-" };

        class Linha
        {
            public LinhaPdf Origem;
            public Tipo Tipo;
            public List<string> Tokens;
            public string Norm;
        }

        static List<string> Tokens(LinhaPdf l)
        {
            // separa por espaço, mas junta "R$" com o número seguinte
            var brutos = string.Join(" ", l.Campos).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            var r = new List<string>();
            for (int i = 0; i < brutos.Count; i++)
            {
                string t = brutos[i];
                if ((t == "R$" || t == "-R$") && i + 1 < brutos.Count && (RxMoeda.IsMatch(brutos[i + 1]) || RxQtd.IsMatch(brutos[i + 1]))) { r.Add((t == "-R$" ? "-" : "") + brutos[i + 1]); i++; }
                else r.Add(t.StartsWith("R$") && t.Length > 2 ? t.Substring(2) : t);
            }
            return r;
        }

        // Relatório "Vendas por Vendedor - Sobre Faturamento": linhas com  R$ valor | % | R$ valor2
        [ThreadStatic] static bool formatoFaturamento;

        static Resumo Interpretar(List<LinhaPdf> origem)
        {
            var r = new Resumo();
            formatoFaturamento = origem.Take(20).Any(l => Normalizar(l.Texto).Contains("FATURAMENTO"));
            r.EhFaturamento = formatoFaturamento;
            var linhas = origem.Select(l => new Linha { Origem = l, Tokens = Tokens(l), Norm = Normalizar(l.Texto) }).ToList();
            foreach (var l in origem) r.TextoLido.Add("[p" + l.Pagina + "]  " + string.Join("  |  ", l.Campos));

            // 1) Cabeçalho de cada página: da primeira linha até a última linha com palavra de cabeçalho
            foreach (var pag in linhas.GroupBy(l => l.Origem.Pagina))
            {
                var lista = pag.ToList();
                int ultimo = -1;
                for (int i = 0; i < Math.Min(lista.Count, 14); i++)
                    if (PALAVRAS_CABECALHO.Any(p => lista[i].Norm.Contains(p))) ultimo = i;
                for (int i = 0; i <= ultimo; i++) lista[i].Tipo = Tipo.Cabecalho;

                if (pag.Key == lista[0].Origem.Pagina && r.Periodo == "" && ultimo >= 0)
                {
                    string cab = string.Join("  ", lista.Take(ultimo + 1).Select(x => x.Origem.Texto));
                    var per = Regex.Match(cab, @"(\d{2}/\d{2}/\d{4})\s*(a|até|ate|-)\s*(\d{2}/\d{2}/\d{4})", RegexOptions.IgnoreCase);
                    if (per.Success) r.Periodo = per.Groups[1].Value + " a " + per.Groups[3].Value;
                    var em = Regex.Match(cab, @"(\d{2}/\d{2}/\d{4})\s+(\d{2}:\d{2}(:\d{2})?)");
                    if (em.Success) r.Emissao = em.Groups[1].Value + " " + em.Groups[2].Value;
                    // empresa: o texto mais longo do cabeçalho sem números e sem palavra de cabeçalho
                    foreach (var l in lista.Take(ultimo + 1))
                        foreach (var campo in l.Origem.Campos)
                        {
                            string n = Normalizar(campo);
                            // aceita nome com número (ex.: 3M), mas não data, hora ou valor
                            bool pareceDado = Regex.IsMatch(campo, @"\d{1,2}/\d{1,2}|\d:\d|R\$|^\d+$") || campo.Count(char.IsLetter) < 5;
                            if (n.Length > 6 && n.Length > r.Empresa.Length && !pareceDado && !PALAVRAS_CABECALHO.Any(p => n.Contains(p)))
                                r.Empresa = campo;
                        }
                }
            }

            // 2) Tipo de cada linha
            foreach (var l in linhas)
            {
                if (l.Tipo == Tipo.Cabecalho) continue;
                l.Tipo = Classificar(l);
            }

            // 3) Monta vendedores e pedidos
            Vendedor atual = null;
            for (int i = 0; i < linhas.Count; i++)
            {
                var l = linhas[i];
                switch (l.Tipo)
                {
                    case Tipo.Cabecalho:
                    case Tipo.Colunas:
                        break;

                    case Tipo.Texto:
                        if (ProximoUtil(linhas, i) == Tipo.Pedido)
                        {
                            atual = LerVendedor(l);
                            r.Vendedores.Add(atual);
                        }
                        else r.NaoReconhecidas.Add("[p" + l.Origem.Pagina + "]  " + l.Origem.Texto);
                        break;

                    case Tipo.Pedido:
                        if (atual == null)
                        {
                            atual = new Vendedor { Nome = "(sem vendedor identificado)" };
                            r.Vendedores.Add(atual);
                        }
                        var p = LerPedido(l);
                        if (p == null) { r.NaoReconhecidas.Add("[p" + l.Origem.Pagina + "]  " + l.Origem.Texto); break; }
                        p.Vendedor = atual;
                        atual.Pedidos.Add(p);
                        break;

                    case Tipo.Subtotal:
                        {
                            double? valor, qtd;
                            NumerosFinais(l.Tokens, out valor, out qtd);
                            if (atual != null && atual.TotalInformado == null) { atual.TotalInformado = valor; atual.QuantidadeInformada = qtd; atual = null; }
                            else if (atual != null) { atual = null; }
                            else r.NaoReconhecidas.Add("[p" + l.Origem.Pagina + "]  " + l.Origem.Texto + "   (total sem vendedor antes)");
                        }
                        break;

                    case Tipo.TotalGeral:
                        {
                            double? valor, qtd;
                            NumerosFinais(l.Tokens, out valor, out qtd);
                            r.TotalGeralInformado = valor;
                            r.QuantidadeGeralInformada = qtd;
                            atual = null;
                        }
                        break;
                }
            }
            r.Vendedores.RemoveAll(v => v.Pedidos.Count == 0 && v.TotalInformado == null);
            r.SemVendedor = r.Vendedores.Where(EhSemVendedor).ToList();
            r.Vendedores.RemoveAll(EhSemVendedor);
            foreach (var v in r.Vendedores.Concat(r.SemVendedor)) v.TotalLido = v.Total;
            return r;
        }

        // Grupo de vendas sem vendedor: pedidos sem cabeçalho de vendedor antes,
        // código só de zeros (0, 000) ou nome do tipo "SEM VENDEDOR" / "NÃO INFORMADO"
        static bool EhSemVendedor(Vendedor v)
        {
            if (v.Nome == "(sem vendedor identificado)") return true;
            string n = Normalizar(v.Nome);
            if (n == "" && string.IsNullOrEmpty(v.Codigo)) return true;
            if (!string.IsNullOrEmpty(v.Codigo) && v.Codigo.Trim('0') == "" && (n == "" || n == "VENDEDOR " + v.Codigo)) return true;
            foreach (var k in new[] { "SEM VENDEDOR", "NAO INFORMADO", "NAO DEFINIDO", "INDEFINIDO", "SEM REPRESENTANTE", "NENHUM VENDEDOR" })
                if (n.Contains(k)) return true;
            return false;
        }

        static Tipo ProximoUtil(List<Linha> linhas, int i)
        {
            for (int k = i + 1; k < linhas.Count; k++)
                if (linhas[k].Tipo != Tipo.Cabecalho && linhas[k].Tipo != Tipo.Colunas) return linhas[k].Tipo;
            return Tipo.Texto;
        }

        static Tipo Classificar(Linha l)
        {
            var t = l.Tokens;
            bool temData = t.Any(x => RxData.IsMatch(x));
            bool temMoeda = t.Any(x => RxMoeda.IsMatch(x));
            var palavras = t.Where(x => !RxMoeda.IsMatch(x) && !RxQtd.IsMatch(x) && !RxPct.IsMatch(x) && !Regex.IsMatch(x, @"^[\d.,/:-]+$")).Select(Normalizar).ToList();

            if (l.Norm.Contains("TOTAL GERAL") || l.Norm.StartsWith("TOTAL GERAL") || (l.Norm.Contains("TOTAL") && l.Norm.Contains("GERAL")))
                return Tipo.TotalGeral;
            if (temData && temMoeda) return Tipo.Pedido;
            if ((temMoeda || t.Any(x => RxQtd.IsMatch(x))) && palavras.All(p => PALAVRAS_SUBTOTAL.Contains(p) || PALAVRAS_SUBTOTAL.Contains(p.TrimEnd(':'))))
                return Tipo.Subtotal;
            if (!temMoeda && palavras.Count > 0 && palavras.All(p => PALAVRAS_COLUNA.Contains(p.TrimEnd('.', ':'))))
                return Tipo.Colunas;
            if (!temMoeda && (l.Norm.Contains("PEDIDO") || l.Norm.Contains("CLIENTE")) && (l.Norm.Contains("VALOR") || l.Norm.Contains("DATA")))
                return Tipo.Colunas;
            return Tipo.Texto;
        }

        static Vendedor LerVendedor(Linha l)
        {
            var t = new List<string>(l.Tokens);
            // tira rótulo "Vendedor:" do começo
            while (t.Count > 0 && (Normalizar(t[0]).TrimEnd(':') == "VENDEDOR" || t[0] == ":" || t[0] == "-")) t.RemoveAt(0);
            double? valor, qtd;
            int fim = NumerosFinais(t, out valor, out qtd);
            t = t.Take(fim).ToList();
            var v = new Vendedor { TotalInformado = null };
            if (t.Count > 0 && Regex.IsMatch(t[0].TrimEnd('-', ':'), @"^\d+$")) { v.Codigo = t[0].TrimEnd('-', ':'); t.RemoveAt(0); }
            while (t.Count > 0 && (t[0] == "-" || t[0] == ":")) t.RemoveAt(0);
            v.Nome = string.Join(" ", t).Trim(' ', '-');
            if (v.Nome == "") v.Nome = "Vendedor " + v.Codigo;
            if (valor.HasValue) { v.TotalInformado = valor; v.QuantidadeInformada = qtd; }
            return v;
        }

        static Pedido LerPedido(Linha l)
        {
            var t = l.Tokens;
            int iData = t.FindIndex(x => RxData.IsMatch(x));
            if (iData < 0) return null;
            double? valor, qtd;
            int fim = NumerosFinais(t, out valor, out qtd);
            if (!valor.HasValue) return LerNotaFaturamento(t, iData);

            var p = new Pedido { Valor = valor.Value, Quantidade = qtd ?? 0 };
            DateTime d;
            string sd = t[iData];
            if (DateTime.TryParseExact(sd, sd.Length == 8 ? "dd/MM/yy" : "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) p.Data = d;

            // nº do pedido: último número antes da data (ou o primeiro token)
            var antes = t.Take(iData).Where(x => Regex.IsMatch(x, @"^[\d./-]+$")).ToList();
            p.Numero = antes.Count > 0 ? antes.Last() : (iData > 0 ? t[0] : "");

            // depois da data: [código do cliente] nome do cliente ... números do fim
            var meio = t.Skip(iData + 1).Take(Math.Max(0, fim - iData - 1)).ToList();
            // se o pedido veio depois da data
            if (p.Numero == "" && meio.Count > 0 && Regex.IsMatch(meio[0], @"^\d+$") && meio.Count > 2) { p.Numero = meio[0]; meio.RemoveAt(0); }
            if (meio.Count > 1 && RxCodigo.IsMatch(meio[0]) && meio[0].Any(char.IsDigit)) { p.CodCliente = meio[0].TrimEnd('-'); meio.RemoveAt(0); }
            while (meio.Count > 0 && (meio[0] == "-" || meio[0] == ":")) meio.RemoveAt(0);
            p.Cliente = string.Join(" ", meio).Trim(' ', '-');
            if (p.Cliente == "") p.Cliente = string.IsNullOrEmpty(p.CodCliente) ? "(sem nome)" : "Cliente " + p.CodCliente;
            return p;
        }

        // Linha do relatório de FATURAMENTO, com o cliente no FIM:
        //   Série | Nota | Data | [Seu Pedido] | R$ Valor de Venda | % Comis. | R$ Valor Comissão | Cliente
        // O valor é o PRIMEIRO R$ depois da data; o cliente é o que vem depois dos números.
        static Pedido LerNotaFaturamento(List<string> t, int iData)
        {
            int ini = -1;
            for (int k = iData + 1; k < t.Count; k++) if (RxMoeda.IsMatch(t[k])) { ini = k; break; }
            if (ini < 0) return null;
            int fimNum = ini;
            while (fimNum < t.Count && (RxMoeda.IsMatch(t[fimNum]) || RxPct.IsMatch(t[fimNum]) || RxQtd.IsMatch(t[fimNum]) || t[fimNum] == "R$")) fimNum++;
            var p = new Pedido { Valor = Num(t[ini]), Quantidade = 0 };
            DateTime d;
            string sd = t[iData];
            if (DateTime.TryParseExact(sd, sd.Length == 8 ? "dd/MM/yy" : "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) p.Data = d;
            // nº da nota: último número antes da data (a série vem antes dela)
            var antes = t.Take(iData).Where(x => Regex.IsMatch(x, @"^[\d./-]+$")).ToList();
            p.Numero = antes.Count > 0 ? antes.Last().TrimStart('0') : "";
            if (p.Numero == "" && antes.Count > 0) p.Numero = "0";
            p.Cliente = string.Join(" ", t.Skip(fimNum)).Trim(' ', '-');
            if (p.Cliente == "") p.Cliente = "(sem nome)";
            return p;
        }

        // Lê, do fim da linha para trás, os números: o último com 2 casas é o valor (R$),
        // um com 3 casas é a quantidade. Devolve onde os números começam.
        // Relatório de FATURAMENTO ("R$ valor  x,xx%  R$ valor2"): quando há um % no meio,
        // o valor é o PRIMEIRO R$ (o que vem antes do %). Vale também para o TOTAL GERAL dele.
        static int NumerosFinais(List<string> t, out double? valor, out double? qtd)
        {
            valor = null; qtd = null;
            var moedas = new List<string>();
            bool temPct = false;
            int i = t.Count;
            while (i > 0)
            {
                string x = t[i - 1];
                if (RxMoeda.IsMatch(x)) { moedas.Add(x); i--; }
                else if (RxQtd.IsMatch(x)) { if (!qtd.HasValue) qtd = Num(x); i--; }
                else if (RxPct.IsMatch(x)) { temPct = true; i--; }
                else if (x == "R$" || x == "-") i--;
                else break;
            }
            if (moedas.Count == 0) return i;
            if (temPct || formatoFaturamento) valor = Num(moedas[moedas.Count - 1]);        // o primeiro da linha
            else
            {
                valor = Num(moedas[0]);
                if (moedas.Count > 1 && !qtd.HasValue) qtd = Num(moedas[1]);
            }
            return i;
        }

        public static double Num(string s)
        {
            s = s.Replace("R$", "").Replace(" ", "");
            bool neg = s.StartsWith("-");
            s = s.TrimStart('-').Replace(".", "").Replace(",", ".");
            double v;
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            return neg ? -v : v;
        }

        public static string Normalizar(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (char ch in s.Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
            return string.Join(" ", sb.ToString().ToUpperInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
