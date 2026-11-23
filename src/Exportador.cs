// =====================================================================
//  Exportador.cs  -  Gera um arquivo .html com o painel e os dados já
//  dentro dele, para mandar por email. Quem recebe abre no navegador
//  (Chrome, Edge, celular) sem instalar nada e sem precisar dos PDFs.
//
//  Como funciona: o modelo da página (painel_modelo.html) vai embutido
//  no próprio .exe. Aqui transformamos os resumos lidos em JSON e
//  colocamos no lugar do marcador  /*DADOS*/null  do modelo.
// =====================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace PainelPedidos
{
    public static class Exportador
    {
        // Uma área do painel (Vendas ou Faturamento) para ir no arquivo
        public class Area
        {
            public string Titulo, Relatorio;
            public bool Notas;
            public List<Resumo> Empresas, Anteriores;   // período mostrado (usado se Periodos for vazio)
            public List<PeriodoExp> Periodos = new List<PeriodoExp>();   // TODOS os períodos lidos
            public int PeriodoInicial;
        }

        public class PeriodoExp
        {
            public string Nome;
            public List<Resumo> Empresas, Anteriores;
        }

        public static string GerarHtml(List<Resumo> empresas, List<Resumo> anteriores, string titulo, string relatorio, bool notas = false)
        {
            return GerarHtml(new List<Area> { new Area { Titulo = titulo, Relatorio = relatorio, Notas = notas, Empresas = empresas, Anteriores = anteriores } }, 0);
        }

        // Várias áreas no MESMO arquivo: no navegador aparece "Vendas  Faturamento" para trocar.
        public static string GerarHtml(List<Area> areas, int inicial, List<string> sala = null)
        {
            string modelo;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("PainelPedidos.painel_modelo.html"))
            {
                if (s == null) throw new InvalidOperationException("Modelo do relatório não encontrado dentro do programa.");
                using (var r = new StreamReader(s, Encoding.UTF8)) modelo = r.ReadToEnd();
            }
            string titulo = areas.Count == 1 ? areas[0].Titulo : "Painel de " + string.Join(" e ", areas.Select(x => x.Titulo.Replace("Painel de ", "")));
            modelo = modelo.Replace("{{TITULO}}", Html(titulo)).Replace("{{RELATORIO}}", "");
            var sb = new StringBuilder();
            sb.Append("{\"gerado\":").Append(Txt(DateTime.Now.ToString("dd/MM/yyyy HH:mm")))
              .Append(",\"inicial\":").Append(inicial)
              .Append(",\"sala\":[").Append(string.Join(",", (sala ?? new List<string>()).Select(Txt))).Append(']')   // Sala de Vendas
              .Append(",\"areas\":[");
            for (int i = 0; i < areas.Count; i++)
            {
                var a = areas[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"titulo\":").Append(Txt(a.Titulo)).Append(",\"relatorio\":").Append(Txt(a.Relatorio))
                  .Append(",\"notas\":").Append(a.Notas ? "true" : "false")
                  .Append(",\"pinicial\":").Append(a.PeriodoInicial)
                  .Append(",\"periodos\":[");
                var pers = a.Periodos != null && a.Periodos.Count > 0 ? a.Periodos
                         : new List<PeriodoExp> { new PeriodoExp { Nome = "", Empresas = a.Empresas, Anteriores = a.Anteriores } };
                for (int k = 0; k < pers.Count; k++)
                {
                    if (k > 0) sb.Append(',');
                    sb.Append("{\"nome\":").Append(Txt(pers[k].Nome));
                    Empresas(sb, "empresas", pers[k].Empresas ?? new List<Resumo>());
                    Empresas(sb, "anteriores", pers[k].Anteriores ?? new List<Resumo>());
                    sb.Append('}');
                }
                sb.Append("]}");
            }
            sb.Append("]}");
            return modelo.Replace("/*DADOS*/null", sb.ToString());
        }

        static string Html(string s)
        {
            return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        static void Empresas(StringBuilder sb, string chave, List<Resumo> empresas)
        {
            sb.Append(",\"" + chave + "\":[");
            for (int i = 0; i < empresas.Count; i++)
            {
                var e = empresas[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"curta\":").Append(Txt(e.EmpresaCurta))
                  .Append(",\"nome\":").Append(Txt(e.Empresa))
                  .Append(",\"periodo\":").Append(Txt(e.Periodo))
                  .Append(",\"emissao\":").Append(Txt(e.Emissao))
                  .Append(",\"arquivo\":").Append(Txt(Path.GetFileName(e.Arquivo)))
                  .Append(",\"ti\":").Append(Num(e.TotalGeralInformado));
                Vendedores(sb, "vend", e.Vendedores.Concat(e.Zerados).ToList());
                Vendedores(sb, "sv", e.SemVendedor);
                sb.Append('}');
            }
            sb.Append(']');
        }

        static void Vendedores(StringBuilder sb, string chave, List<Vendedor> lista)
        {
                sb.Append(",\"" + chave + "\":[");
                for (int k = 0; k < lista.Count; k++)
                {
                    var v = lista[k];
                    if (k > 0) sb.Append(',');
                    sb.Append("{\"c\":").Append(Txt(v.Codigo ?? "")).Append(",\"n\":").Append(Txt(v.Nome))
                      .Append(",\"ti\":").Append(Num(v.TotalInformado))
                      .Append(",\"m\":").Append(v.Manual ? "1" : "0")                 // criado no programa (licitação)
                      .Append(",\"tl\":").Append(Num(v.TotalLido))                    // total como veio no PDF
                      .Append(",\"tr\":").Append(Num(v.Transferido))                  // quanto foi p/ licitação
                      .Append(",\"p\":[");
                    for (int j = 0; j < v.Pedidos.Count; j++)
                    {
                        var p = v.Pedidos[j];
                        if (j > 0) sb.Append(',');
                        sb.Append('[').Append(Txt(p.Numero)).Append(',')
                          .Append(Txt(p.Data.HasValue ? p.Data.Value.ToString("dd/MM/yyyy") : "")).Append(',')
                          .Append(Txt(p.CodCliente ?? "")).Append(',').Append(Txt(p.Cliente)).Append(',')
                          .Append(Num(p.Quantidade)).Append(',').Append(Num(p.Valor)).Append(',')
                          .Append(p.Licitacao ? "1" : "0").Append(',').Append(Txt(p.VendedorOriginal ?? "")).Append(']');
                    }
                    sb.Append("]}");
                }
                sb.Append(']');
        }

        static string Num(double? v)
        {
            return v.HasValue ? Math.Round(v.Value, 4).ToString("0.####", CultureInfo.InvariantCulture) : "null";
        }

        // Texto em JSON, com escape (e "</" vira "<\/" para não fechar o <script> da página)
        static string Txt(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '/': sb.Append("\\/"); break;
                    default:
                        if (c < 32 || c == (char)0x2028 || c == (char)0x2029) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
