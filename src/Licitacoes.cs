// =====================================================================
//  Licitacoes.cs  -  Clientes (razão social) cujas vendas são LICITAÇÃO.
//
//  A venda continua com o vendedor do PDF; ela só fica MARCADA como
//  licitação, para diferenciar nas telas e nos totais.
//  Fica salvo em PainelPedidos_licitacoes.txt, ao lado do programa
//  (quem usa o mesmo .exe da pasta pública vê as mesmas marcações).
//  Uma razão social por linha. Toda venda para ela, em qualquer empresa
//  e em qualquer semana, já vem marcada automaticamente.
// =====================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace PainelPedidos
{
    public class RegraLicitacao
    {
        public string Cliente;   // razão social como aparece no PDF
        public string Chave { get { return Resumo.Normalizar(Cliente); } }
    }

    public static class Licitacoes
    {
        public static string Arquivo
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "PainelPedidos_licitacoes.txt"); }
        }

        public static List<RegraLicitacao> Carregar()
        {
            var r = new List<RegraLicitacao>();
            try
            {
                if (!File.Exists(Arquivo)) return r;
                foreach (var bruta in File.ReadAllLines(Arquivo, Encoding.UTF8))
                {
                    string l = bruta.Trim();
                    if (l == "" || l.StartsWith("#")) continue;
                    if (l.Contains(";")) l = l.Substring(0, l.IndexOf(';')).Trim();   // formato antigo "cliente ; vendedor"
                    var regra = new RegraLicitacao { Cliente = l };
                    if (regra.Chave == "" || r.Any(x => x.Chave == regra.Chave)) continue;
                    r.Add(regra);
                }
            }
            catch { }
            return r;
        }

        public static void Salvar(List<RegraLicitacao> regras)
        {
            var linhas = new List<string>
            {
                "# Painel de Pedidos - clientes de LICITAÇÃO (uma razão social por linha)",
                "# Toda venda para estas razões sociais fica marcada como licitação (continua no vendedor do PDF).",
                ""
            };
            linhas.AddRange(regras.OrderBy(x => x.Cliente).Select(x => x.Cliente));
            File.WriteAllLines(Arquivo, linhas, new UTF8Encoding(true));
        }

        // O ERP corta razões sociais longas, e cada relatório pode cortar num ponto diferente
        // ("BRENO CAMPOS GOUVEIA CONSTRUCAO &" x "BRENO CAMPOS GOUVEIA CONSTRUCAO & INCORPORACAO").
        // Então vale nome igual OU um começar com o outro (se o menor tiver pelo menos 15 letras).
        public static bool Bate(string chaveRegra, string chaveCliente)
        {
            if (chaveRegra == chaveCliente) return true;
            string curto = chaveRegra.Length <= chaveCliente.Length ? chaveRegra : chaveCliente;
            string longo = curto == chaveRegra ? chaveCliente : chaveRegra;
            return curto.Length >= 15 && longo.StartsWith(curto);
        }

        public static bool EhLicitacao(List<RegraLicitacao> regras, string cliente)
        {
            string k = Resumo.Normalizar(cliente);
            return regras.Any(x => Bate(x.Chave, k));
        }

        // Devolve uma CÓPIA do resumo com as vendas de licitação marcadas
        public static Resumo Aplicar(Resumo original, List<RegraLicitacao> regras)
        {
            var r = original.Clonar();
            foreach (var p in r.Vendedores.SelectMany(v => v.Pedidos).Concat(r.SemVendedor.SelectMany(v => v.Pedidos)))
                p.Licitacao = EhLicitacao(regras, p.Cliente);
            return r;
        }
    }
}

namespace PainelPedidos
{
    // =====================================================================
    //  SALA DE VENDAS: os vendedores escolhidos (fixo até mudar).
    //  Arquivo PainelPedidos_sala.txt ao lado do .exe, um vendedor por linha.
    //  Vale para Vendas e Faturamento (o vendedor é reconhecido pelo nome).
    // =====================================================================
    static class SalaVendas
    {
        public static string Arquivo
        {
            get { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "PainelPedidos_sala.txt"); }
        }

        public static System.Collections.Generic.List<string> Carregar()
        {
            var r = new System.Collections.Generic.List<string>();
            try
            {
                if (!System.IO.File.Exists(Arquivo)) return r;
                foreach (var l in System.IO.File.ReadAllLines(Arquivo, System.Text.Encoding.UTF8))
                {
                    string t = l.Trim();
                    if (t == "" || t.StartsWith(";")) continue;
                    if (!r.Exists(x => Resumo.Normalizar(x) == Resumo.Normalizar(t))) r.Add(t);
                }
            }
            catch { }
            return r;
        }

        public static void Salvar(System.Collections.Generic.IEnumerable<string> nomes)
        {
            var linhas = new System.Collections.Generic.List<string> { "; Vendedores da SALA DE VENDAS (um por linha). Pode editar aqui ou pelo programa." };
            linhas.AddRange(nomes);
            System.IO.File.WriteAllLines(Arquivo, linhas.ToArray(), new System.Text.UTF8Encoding(true));
        }
    }
}
