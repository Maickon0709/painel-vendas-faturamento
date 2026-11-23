// =====================================================================
//  PdfTexto.cs  -  Extrai o TEXTO de um PDF, com a posição de cada pedaço,
//  sem nenhuma biblioteca externa (só o que já vem no .NET do Windows).
//
//  Como um PDF guarda texto (resumindo):
//   - O arquivo é uma lista de "objetos" numerados (1 0 obj ... endobj).
//   - Cada página aponta para um "conteúdo": uma sequência de comandos
//     de desenho, geralmente comprimida (FlateDecode = zip).
//   - Os comandos de texto dizem: "use a fonte F1, vá para (x,y),
//     escreva estes códigos". Os códigos são traduzidos para letras
//     pela tabela da fonte (ToUnicode ou Encoding).
//  Aqui juntamos tudo e devolvemos, por página, as LINHAS de texto,
//  com os campos separados por TAB quando há espaço grande entre eles.
// =====================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PainelPedidos
{
    // ---------------- Tipos de objeto do PDF ----------------
    class PdfNome { public string Valor; public PdfNome(string v) { Valor = v; } public override string ToString() { return "/" + Valor; } }
    class PdfRef { public int Num; public PdfRef(int n) { Num = n; } }
    class PdfTextoBytes { public byte[] Bytes; public PdfTextoBytes(byte[] b) { Bytes = b; } }
    class PdfOperador { public string Nome; public PdfOperador(string n) { Nome = n; } }
    class PdfDict : Dictionary<string, object> { }
    class PdfStream { public PdfDict Dict; public byte[] Bruto; }

    // ---------------- Lexer/parser de objetos ----------------
    class PdfLexer
    {
        public readonly byte[] D;
        public int P;
        public PdfLexer(byte[] d, int p) { D = d; P = p; }

        public static bool Espaco(byte b) { return b == 32 || b == 10 || b == 13 || b == 9 || b == 12 || b == 0; }
        public static bool Delim(byte b) { return b == '(' || b == ')' || b == '<' || b == '>' || b == '[' || b == ']' || b == '{' || b == '}' || b == '/' || b == '%'; }

        public void PularEspacos()
        {
            while (P < D.Length)
            {
                if (Espaco(D[P])) P++;
                else if (D[P] == '%') { while (P < D.Length && D[P] != 10 && D[P] != 13) P++; }
                else break;
            }
        }

        public bool Fim { get { PularEspacos(); return P >= D.Length; } }

        // Lê um objeto (número, nome, string, array, dicionário, ref, operador)
        public object Ler()
        {
            PularEspacos();
            if (P >= D.Length) return null;
            byte c = D[P];
            if (c == '/') { P++; int i = P; while (P < D.Length && !Espaco(D[P]) && !Delim(D[P])) P++; return new PdfNome(DecodificarNome(Encoding.GetEncoding(28591).GetString(D, i, P - i))); }
            if (c == '(') return new PdfTextoBytes(LerLiteral());
            if (c == '<')
            {
                if (P + 1 < D.Length && D[P + 1] == '<')
                {
                    P += 2; var dict = new PdfDict();
                    while (true)
                    {
                        PularEspacos();
                        if (P >= D.Length) break;
                        if (D[P] == '>' && P + 1 < D.Length && D[P + 1] == '>') { P += 2; break; }
                        var chave = Ler() as PdfNome;
                        if (chave == null) { continue; }
                        dict[chave.Valor] = Ler();
                    }
                    return dict;
                }
                return new PdfTextoBytes(LerHex());
            }
            if (c == '[')
            {
                P++; var lista = new List<object>();
                while (true)
                {
                    PularEspacos();
                    if (P >= D.Length) break;
                    if (D[P] == ']') { P++; break; }
                    lista.Add(Ler());
                }
                return lista;
            }
            if (c == ']' || c == '>' || c == ')' || c == '}' || c == '{') { P++; return new PdfOperador(((char)c).ToString()); }

            // número, ref ou palavra/operador
            int ini = P;
            while (P < D.Length && !Espaco(D[P]) && !Delim(D[P])) P++;
            string tok = Encoding.GetEncoding(28591).GetString(D, ini, P - ini);
            double num;
            if (EhNumero(tok) && double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out num))
            {
                // pode ser "12 0 R"
                int salvo = P;
                if (tok.IndexOf('.') < 0 && !tok.StartsWith("-") && !tok.StartsWith("+"))
                {
                    PularEspacos();
                    int i2 = P;
                    while (P < D.Length && D[P] >= '0' && D[P] <= '9') P++;
                    if (P > i2)
                    {
                        PularEspacos();
                        if (P < D.Length && D[P] == 'R' && (P + 1 >= D.Length || Espaco(D[P + 1]) || Delim(D[P + 1])))
                        { P++; return new PdfRef((int)num); }
                    }
                    P = salvo;
                }
                return num;
            }
            if (tok == "true") return true;
            if (tok == "false") return false;
            if (tok == "null") return null;
            return new PdfOperador(tok);
        }

        static bool EhNumero(string t)
        {
            if (t.Length == 0) return false;
            foreach (char ch in t) if (!(char.IsDigit(ch) || ch == '.' || ch == '-' || ch == '+')) return false;
            return t.Any(char.IsDigit);
        }

        static string DecodificarNome(string s)
        {
            if (s.IndexOf('#') < 0) return s;
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '#' && i + 2 < s.Length) { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 2), 16)); i += 2; }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        byte[] LerLiteral()
        {
            P++; // (
            var ms = new List<byte>();
            int nivel = 1;
            while (P < D.Length)
            {
                byte b = D[P++];
                if (b == '\\')
                {
                    if (P >= D.Length) break;
                    byte e = D[P++];
                    switch (e)
                    {
                        case (byte)'n': ms.Add(10); break;
                        case (byte)'r': ms.Add(13); break;
                        case (byte)'t': ms.Add(9); break;
                        case (byte)'b': ms.Add(8); break;
                        case (byte)'f': ms.Add(12); break;
                        case 13: if (P < D.Length && D[P] == 10) P++; break;
                        case 10: break;
                        default:
                            if (e >= '0' && e <= '7')
                            {
                                int v = e - '0';
                                for (int k = 0; k < 2 && P < D.Length && D[P] >= '0' && D[P] <= '7'; k++) v = v * 8 + (D[P++] - '0');
                                ms.Add((byte)v);
                            }
                            else ms.Add(e);
                            break;
                    }
                }
                else if (b == '(') { nivel++; ms.Add(b); }
                else if (b == ')') { nivel--; if (nivel == 0) break; ms.Add(b); }
                else ms.Add(b);
            }
            return ms.ToArray();
        }

        byte[] LerHex()
        {
            P++; // <
            var sb = new StringBuilder();
            while (P < D.Length && D[P] != '>') { char ch = (char)D[P++]; if (Uri.IsHexDigit(ch)) sb.Append(ch); }
            P++;
            if (sb.Length % 2 == 1) sb.Append('0');
            var r = new byte[sb.Length / 2];
            for (int i = 0; i < r.Length; i++) r[i] = Convert.ToByte(sb.ToString(i * 2, 2), 16);
            return r;
        }
    }

    // ---------------- Item de texto posicionado ----------------
    class ItemTexto
    {
        public double X, Y, XFim, Tamanho;
        public string Texto;
    }

    public class LinhaPdf
    {
        public int Pagina;
        public List<string> Campos = new List<string>();
        public string Texto { get { return string.Join("   ", Campos); } }
    }

    // ---------------- Fonte: códigos -> letras e larguras ----------------
    class FontePdf
    {
        public bool DoisBytes;
        public Dictionary<int, string> Mapa = new Dictionary<int, string>();  // ToUnicode
        public string[] Codificacao;                                         // códigos de 1 byte
        public Dictionary<int, double> Larguras = new Dictionary<int, double>();
        public double LarguraPadrao = 500;

        public List<KeyValuePair<int, string>> Decodificar(byte[] b)
        {
            var r = new List<KeyValuePair<int, string>>();
            if (DoisBytes)
            {
                for (int i = 0; i + 1 < b.Length; i += 2)
                {
                    int cod = (b[i] << 8) | b[i + 1];
                    string s;
                    if (!Mapa.TryGetValue(cod, out s)) s = cod < 0x20 ? "" : ((char)cod).ToString();
                    r.Add(new KeyValuePair<int, string>(cod, s));
                }
            }
            else
            {
                foreach (byte x in b)
                {
                    string s;
                    if (!Mapa.TryGetValue(x, out s)) s = Codificacao != null && Codificacao[x] != null ? Codificacao[x] : Win1252(x);
                    r.Add(new KeyValuePair<int, string>(x, s));
                }
            }
            return r;
        }

        public double Largura(int cod)
        {
            double w;
            return Larguras.TryGetValue(cod, out w) ? w : LarguraPadrao;
        }

        public static string Win1252(byte x)
        {
            if (x < 32) return "";
            return Encoding.GetEncoding(1252).GetString(new[] { x });
        }
    }

    // ---------------- O leitor ----------------
    public class PdfTexto
    {
        readonly byte[] dados;
        readonly Dictionary<int, int> deslocamentos = new Dictionary<int, int>();
        readonly Dictionary<int, object> cache = new Dictionary<int, object>();
        readonly Dictionary<int, byte[]> objStmDados = new Dictionary<int, byte[]>();
        readonly Dictionary<int, int> objStmPos = new Dictionary<int, int>();

        PdfTexto(byte[] d) { dados = d; }

        // Ponto de entrada: devolve as linhas de todas as páginas, na ordem
        public static List<LinhaPdf> LerLinhas(byte[] pdf)
        {
            var leitor = new PdfTexto(pdf);
            leitor.Indexar();
            var linhas = new List<LinhaPdf>();
            int numPag = 0;
            foreach (var pagina in leitor.Paginas())
            {
                numPag++;
                List<ItemTexto> itens;
                try { itens = leitor.ExtrairItens(pagina); }
                catch { itens = new List<ItemTexto>(); }
                linhas.AddRange(MontarLinhas(itens, numPag));
            }
            return linhas;
        }

        // ---------- índice de objetos ----------
        void Indexar()
        {
            string latin = Encoding.GetEncoding(28591).GetString(dados);
            foreach (Match m in Regex.Matches(latin, @"(?<![0-9])(\d+)\s+(\d+)\s+obj\b"))
                deslocamentos[int.Parse(m.Groups[1].Value)] = m.Index + m.Length;

            // Object streams (PDF 1.5+): objetos guardados comprimidos dentro de outro objeto
            foreach (var num in deslocamentos.Keys.ToList())
            {
                var st = Objeto(num) as PdfStream;
                if (st == null || !EhNome(Valor(st.Dict, "Type"), "ObjStm")) continue;
                byte[] dec;
                try { dec = Decodificar(st); } catch { continue; }
                int n = (int)Numero(Valor(st.Dict, "N")), first = (int)Numero(Valor(st.Dict, "First"));
                var lx = new PdfLexer(dec, 0);
                for (int i = 0; i < n; i++)
                {
                    object a = lx.Ler(), b = lx.Ler();
                    if (!(a is double) || !(b is double)) break;
                    int objNum = (int)(double)a;
                    if (deslocamentos.ContainsKey(objNum)) continue;
                    objStmDados[objNum] = dec;
                    objStmPos[objNum] = first + (int)(double)b;
                }
            }
        }

        object Objeto(int num)
        {
            object o;
            if (cache.TryGetValue(num, out o)) return o;
            cache[num] = null; // evita laço infinito
            int pos;
            if (deslocamentos.TryGetValue(num, out pos))
            {
                var lx = new PdfLexer(dados, pos);
                o = lx.Ler();
                var dict = o as PdfDict;
                if (dict != null)
                {
                    int depois = lx.P;
                    lx.PularEspacos();
                    if (lx.P + 6 <= dados.Length && Encoding.ASCII.GetString(dados, lx.P, 6) == "stream")
                    {
                        int ini = lx.P + 6;
                        if (ini < dados.Length && dados[ini] == 13) ini++;
                        if (ini < dados.Length && dados[ini] == 10) ini++;
                        int tam = -1;
                        object len = Resolver(Valor(dict, "Length"));
                        if (len is double) tam = (int)(double)len;
                        if (tam < 0 || ini + tam > dados.Length || !TemEndstream(ini + tam))
                        {
                            int fim = IndiceDe(dados, "endstream", ini);
                            tam = (fim < 0 ? dados.Length : fim) - ini;
                            while (tam > 0 && (dados[ini + tam - 1] == 10 || dados[ini + tam - 1] == 13)) tam--;
                        }
                        var bruto = new byte[Math.Max(0, tam)];
                        Array.Copy(dados, ini, bruto, 0, bruto.Length);
                        o = new PdfStream { Dict = dict, Bruto = bruto };
                    }
                    else lx.P = depois;
                }
            }
            else if (objStmPos.TryGetValue(num, out pos))
                o = new PdfLexer(objStmDados[num], pos).Ler();
            cache[num] = o;
            return o;
        }

        bool TemEndstream(int p)
        {
            int lim = Math.Min(dados.Length, p + 20);
            for (int i = p; i < lim; i++)
                if (dados[i] == 'e') return i + 9 <= dados.Length && Encoding.ASCII.GetString(dados, i, 9) == "endstream";
            return false;
        }

        static int IndiceDe(byte[] d, string s, int ini)
        {
            var alvo = Encoding.ASCII.GetBytes(s);
            for (int i = ini; i <= d.Length - alvo.Length; i++)
            {
                int k = 0;
                while (k < alvo.Length && d[i + k] == alvo[k]) k++;
                if (k == alvo.Length) return i;
            }
            return -1;
        }

        object Resolver(object o)
        {
            int guarda = 0;
            while (o is PdfRef && guarda++ < 20) o = Objeto(((PdfRef)o).Num);
            return o;
        }

        static object Valor(PdfDict d, string chave)
        {
            object o;
            return d != null && d.TryGetValue(chave, out o) ? o : null;
        }

        object ValorR(PdfDict d, string chave) { return Resolver(Valor(d, chave)); }

        static bool EhNome(object o, string nome) { var n = o as PdfNome; return n != null && n.Valor == nome; }

        static double Numero(object o) { return o is double ? (double)o : 0; }

        PdfDict DictDe(object o)
        {
            o = Resolver(o);
            if (o is PdfStream) return ((PdfStream)o).Dict;
            return o as PdfDict;
        }

        // ---------- filtros de descompressão ----------
        byte[] Decodificar(PdfStream st)
        {
            byte[] b = st.Bruto;
            object f = ValorR(st.Dict, "Filter");
            var filtros = new List<string>();
            if (f is PdfNome) filtros.Add(((PdfNome)f).Valor);
            else if (f is List<object>) foreach (var x in (List<object>)f) { var n = Resolver(x) as PdfNome; if (n != null) filtros.Add(n.Valor); }

            object parms = ValorR(st.Dict, "DecodeParms");
            for (int i = 0; i < filtros.Count; i++)
            {
                string nome = filtros[i];
                if (nome == "FlateDecode" || nome == "Fl")
                {
                    b = Inflar(b);
                    var p = parms is List<object> ? DictDe(((List<object>)parms)[i]) : DictDe(parms);
                    double pred = Numero(Resolver(Valor(p, "Predictor")));
                    if (pred >= 10)
                    {
                        int col = (int)Math.Max(1, Numero(Resolver(Valor(p, "Columns"))));
                        int cores = (int)Math.Max(1, Numero(Resolver(Valor(p, "Colors"))));
                        int bpc = (int)Math.Max(8, Numero(Resolver(Valor(p, "BitsPerComponent"))));
                        b = DesfazerPng(b, col * cores * bpc / 8, cores * bpc / 8);
                    }
                }
                else if (nome == "ASCIIHexDecode" || nome == "AHx") b = new PdfLexer(Concat(new[] { (byte)'<' }, b, new[] { (byte)'>' }), 0).Ler() is PdfTextoBytes ? ((PdfTextoBytes)new PdfLexer(Concat(new[] { (byte)'<' }, b, new[] { (byte)'>' }), 0).Ler()).Bytes : b;
                else if (nome == "ASCII85Decode" || nome == "A85") b = Ascii85(b);
                else if (nome == "LZWDecode" || nome == "LZW") b = Lzw(b);
                else return new byte[0]; // imagens (DCT etc.) não interessam
            }
            return b;
        }

        static byte[] Concat(params byte[][] partes)
        {
            var r = new byte[partes.Sum(p => p.Length)];
            int o = 0;
            foreach (var p in partes) { Array.Copy(p, 0, r, o, p.Length); o += p.Length; }
            return r;
        }

        static byte[] Inflar(byte[] b)
        {
            int ini = 0;
            if (b.Length > 2 && (b[0] & 0x0F) == 8 && ((b[0] << 8) | b[1]) % 31 == 0) ini = 2; // cabeçalho zlib
            using (var ms = new MemoryStream(b, ini, b.Length - ini))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            using (var saida = new MemoryStream())
            {
                var buf = new byte[8192];
                try
                {
                    int n;
                    while ((n = ds.Read(buf, 0, buf.Length)) > 0) saida.Write(buf, 0, n);
                }
                catch (InvalidDataException) { } // final corrompido: fica com o que deu
                return saida.ToArray();
            }
        }

        static byte[] DesfazerPng(byte[] b, int larguraLinha, int bpp)
        {
            var saida = new List<byte>();
            var anterior = new byte[larguraLinha];
            for (int p = 0; p + larguraLinha < b.Length + 1 && p < b.Length; p += larguraLinha + 1)
            {
                int tipo = b[p];
                var linha = new byte[larguraLinha];
                for (int i = 0; i < larguraLinha && p + 1 + i < b.Length; i++)
                {
                    int x = b[p + 1 + i], a = i >= bpp ? linha[i - bpp] : 0, c = anterior[i], d = i >= bpp ? anterior[i - bpp] : 0;
                    int v;
                    switch (tipo)
                    {
                        case 1: v = x + a; break;
                        case 2: v = x + c; break;
                        case 3: v = x + (a + c) / 2; break;
                        case 4: int pa = Math.Abs(c - d), pb = Math.Abs(a - d), pc = Math.Abs(a + c - 2 * d); v = x + (pa <= pb && pa <= pc ? a : pb <= pc ? c : d); break;
                        default: v = x; break;
                    }
                    linha[i] = (byte)v;
                }
                saida.AddRange(linha);
                anterior = linha;
            }
            return saida.ToArray();
        }

        static byte[] Ascii85(byte[] b)
        {
            var r = new List<byte>();
            var grupo = new List<int>();
            foreach (byte c in b)
            {
                if (c == '~') break;
                if (PdfLexer.Espaco(c)) continue;
                if (c == 'z' && grupo.Count == 0) { r.AddRange(new byte[4]); continue; }
                grupo.Add(c - 33);
                if (grupo.Count == 5) { r.AddRange(Bloco85(grupo, 4)); grupo.Clear(); }
            }
            if (grupo.Count > 1) { int n = grupo.Count - 1; while (grupo.Count < 5) grupo.Add(84); r.AddRange(Bloco85(grupo, n)); }
            return r.ToArray();
        }

        static IEnumerable<byte> Bloco85(List<int> g, int n)
        {
            long v = 0;
            foreach (int x in g) v = v * 85 + x;
            var bytes = new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
            return bytes.Take(n);
        }

        static byte[] Lzw(byte[] b)
        {
            var saida = new List<byte>();
            var tabela = new List<byte[]>();
            Action reset = () => { tabela.Clear(); for (int i = 0; i < 256; i++) tabela.Add(new[] { (byte)i }); tabela.Add(null); tabela.Add(null); };
            reset();
            int bits = 9, buffer = 0, nbits = 0;
            byte[] anterior = null;
            foreach (byte x in b)
            {
                buffer = (buffer << 8) | x; nbits += 8;
                while (nbits >= bits)
                {
                    int cod = (buffer >> (nbits - bits)) & ((1 << bits) - 1);
                    nbits -= bits;
                    if (cod == 256) { reset(); bits = 9; anterior = null; continue; }
                    if (cod == 257) return saida.ToArray();
                    byte[] ent;
                    if (cod < tabela.Count && tabela[cod] != null) ent = tabela[cod];
                    else if (anterior != null) ent = anterior.Concat(new[] { anterior[0] }).ToArray();
                    else return saida.ToArray();
                    saida.AddRange(ent);
                    if (anterior != null) tabela.Add(anterior.Concat(new[] { ent[0] }).ToArray());
                    anterior = ent;
                    if (tabela.Count + 1 >= (1 << bits) && bits < 12) bits++;
                }
            }
            return saida.ToArray();
        }

        // ---------- páginas ----------
        IEnumerable<PdfDict> Paginas()
        {
            PdfDict raiz = null;
            // catálogo: pelo trailer, ou procurando /Type /Catalog
            foreach (var num in deslocamentos.Keys.Concat(objStmPos.Keys).OrderByDescending(n => n))
            {
                var d = DictDe(Objeto(num));
                if (d != null && EhNome(Valor(d, "Type"), "Catalog")) { raiz = d; break; }
            }
            var resultado = new List<PdfDict>();
            if (raiz != null) Percorrer(DictDe(Valor(raiz, "Pages")), null, resultado, 0);
            if (resultado.Count == 0) // plano B: todas as páginas, na ordem do arquivo
                foreach (var num in deslocamentos.OrderBy(k => k.Value).Select(k => k.Key))
                {
                    var d = DictDe(Objeto(num));
                    if (d != null && EhNome(Valor(d, "Type"), "Page")) resultado.Add(d);
                }
            return resultado;
        }

        void Percorrer(PdfDict no, object recursosHerdados, List<PdfDict> saida, int prof)
        {
            if (no == null || prof > 50) return;
            object rec = Valor(no, "Resources") ?? recursosHerdados;
            if (EhNome(Valor(no, "Type"), "Pages") || Valor(no, "Kids") != null)
            {
                var kids = Resolver(Valor(no, "Kids")) as List<object>;
                if (kids != null) foreach (var k in kids) Percorrer(DictDe(k), rec, saida, prof + 1);
            }
            else
            {
                if (Valor(no, "Resources") == null && rec != null) no["Resources"] = rec;
                saida.Add(no);
            }
        }

        // ---------- fontes ----------
        readonly Dictionary<PdfDict, FontePdf> fontesCache = new Dictionary<PdfDict, FontePdf>();

        FontePdf CarregarFonte(PdfDict f)
        {
            FontePdf fonte;
            if (f == null) return new FontePdf();
            if (fontesCache.TryGetValue(f, out fonte)) return fonte;
            fonte = new FontePdf();
            fontesCache[f] = fonte;

            bool tipo0 = EhNome(Valor(f, "Subtype"), "Type0");
            fonte.DoisBytes = tipo0;

            // ToUnicode
            var tu = Resolver(Valor(f, "ToUnicode")) as PdfStream;
            if (tu != null)
            {
                try { LerCMap(Encoding.GetEncoding(28591).GetString(Decodificar(tu)), fonte); } catch { }
            }

            if (tipo0)
            {
                var desc = Resolver(Valor(f, "DescendantFonts")) as List<object>;
                var cid = desc != null && desc.Count > 0 ? DictDe(desc[0]) : null;
                if (cid != null)
                {
                    object dw = ValorR(cid, "DW");
                    fonte.LarguraPadrao = dw is double ? (double)dw : 1000;
                    var w = Resolver(Valor(cid, "W")) as List<object>;
                    if (w != null)
                    {
                        int i = 0;
                        while (i < w.Count)
                        {
                            object a = Resolver(w[i]);
                            if (!(a is double)) { i++; continue; }
                            int c1 = (int)(double)a;
                            object b = i + 1 < w.Count ? Resolver(w[i + 1]) : null;
                            if (b is List<object>)
                            {
                                var arr = (List<object>)b;
                                for (int k = 0; k < arr.Count; k++) fonte.Larguras[c1 + k] = Numero(Resolver(arr[k]));
                                i += 2;
                            }
                            else if (b is double && i + 2 < w.Count)
                            {
                                int c2 = (int)(double)b; double larg = Numero(Resolver(w[i + 2]));
                                for (int c = c1; c <= c2 && c - c1 < 70000; c++) fonte.Larguras[c] = larg;
                                i += 3;
                            }
                            else break;
                        }
                    }
                }
            }
            else
            {
                // Encoding (WinAnsi + Differences)
                object enc = ValorR(f, "Encoding");
                var tabela = new string[256];
                for (int i = 32; i < 256; i++) tabela[i] = FontePdf.Win1252((byte)i);
                var encDict = enc as PdfDict;
                if (encDict != null)
                {
                    var dif = Resolver(Valor(encDict, "Differences")) as List<object>;
                    if (dif != null)
                    {
                        int cod = 0;
                        foreach (var item in dif)
                        {
                            object x = Resolver(item);
                            if (x is double) cod = (int)(double)x;
                            else if (x is PdfNome && cod < 256) { string s = NomeGlifo(((PdfNome)x).Valor); if (s != null) tabela[cod] = s; cod++; }
                        }
                    }
                }
                fonte.Codificacao = tabela;

                var larguras = Resolver(Valor(f, "Widths")) as List<object>;
                int primeiro = (int)Numero(ValorR(f, "FirstChar"));
                if (larguras != null)
                    for (int i = 0; i < larguras.Count; i++) fonte.Larguras[primeiro + i] = Numero(Resolver(larguras[i]));
                // fontes padrão (Helvetica etc.) sem Widths: largura média
                fonte.LarguraPadrao = 520;
            }
            return fonte;
        }

        static void LerCMap(string cmap, FontePdf fonte)
        {
            // tamanho do código pelo codespacerange
            var cs = Regex.Match(cmap, @"begincodespacerange\s*<([0-9A-Fa-f]+)>");
            if (cs.Success) fonte.DoisBytes = cs.Groups[1].Value.Length >= 4;

            foreach (Match bloco in Regex.Matches(cmap, @"beginbfchar(.*?)endbfchar", RegexOptions.Singleline))
                foreach (Match m in Regex.Matches(bloco.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]*)>"))
                    fonte.Mapa[Convert.ToInt32(m.Groups[1].Value, 16)] = Utf16(m.Groups[2].Value);

            foreach (Match bloco in Regex.Matches(cmap, @"beginbfrange(.*?)endbfrange", RegexOptions.Singleline))
            {
                foreach (Match m in Regex.Matches(bloco.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*(<([0-9A-Fa-f]*)>|\[(.*?)\])", RegexOptions.Singleline))
                {
                    int lo = Convert.ToInt32(m.Groups[1].Value, 16), hi = Convert.ToInt32(m.Groups[2].Value, 16);
                    if (hi - lo > 70000) continue;
                    if (m.Groups[4].Success)
                    {
                        string dst = m.Groups[4].Value;
                        if (dst.Length == 0) continue;
                        // incrementa o último caractere
                        string prefixo = dst.Length > 4 ? dst.Substring(0, dst.Length - 4) : "";
                        int baseCod = Convert.ToInt32(dst.Substring(Math.Max(0, dst.Length - 4)), 16);
                        for (int c = lo; c <= hi; c++)
                            fonte.Mapa[c] = Utf16(prefixo) + char.ConvertFromUtf32(Math.Min(0x10FFFF, baseCod + (c - lo)));
                    }
                    else
                    {
                        var itens = Regex.Matches(m.Groups[5].Value, @"<([0-9A-Fa-f]*)>");
                        for (int k = 0; k < itens.Count && lo + k <= hi; k++) fonte.Mapa[lo + k] = Utf16(itens[k].Groups[1].Value);
                    }
                }
            }
        }

        static string Utf16(string hex)
        {
            if (hex.Length < 4) return hex.Length == 0 ? "" : ((char)Convert.ToInt32(hex, 16)).ToString();
            var sb = new StringBuilder();
            for (int i = 0; i + 3 < hex.Length; i += 4) sb.Append((char)Convert.ToInt32(hex.Substring(i, 4), 16));
            return sb.ToString();
        }

        static readonly Dictionary<string, string> GLIFOS = new Dictionary<string, string>
        {
            {"space"," "},{"exclam","!"},{"quotedbl","\""},{"numbersign","#"},{"dollar","$"},{"percent","%"},{"ampersand","&"},
            {"quotesingle","'"},{"parenleft","("},{"parenright",")"},{"asterisk","*"},{"plus","+"},{"comma",","},{"hyphen","-"},
            {"period","."},{"slash","/"},{"zero","0"},{"one","1"},{"two","2"},{"three","3"},{"four","4"},{"five","5"},{"six","6"},
            {"seven","7"},{"eight","8"},{"nine","9"},{"colon",":"},{"semicolon",";"},{"less","<"},{"equal","="},{"greater",">"},
            {"question","?"},{"at","@"},{"bracketleft","["},{"backslash","\\"},{"bracketright","]"},{"underscore","_"},
            {"ordfeminine","ª"},{"ordmasculine","º"},{"degree","°"},{"endash","–"},{"emdash","—"},{"quoteright","’"},{"quoteleft","‘"},
            {"Aacute","Á"},{"Agrave","À"},{"Acircumflex","Â"},{"Atilde","Ã"},{"Ccedilla","Ç"},{"Eacute","É"},{"Ecircumflex","Ê"},
            {"Iacute","Í"},{"Oacute","Ó"},{"Ocircumflex","Ô"},{"Otilde","Õ"},{"Uacute","Ú"},{"Udieresis","Ü"},
            {"aacute","á"},{"agrave","à"},{"acircumflex","â"},{"atilde","ã"},{"ccedilla","ç"},{"eacute","é"},{"ecircumflex","ê"},
            {"iacute","í"},{"oacute","ó"},{"ocircumflex","ô"},{"otilde","õ"},{"uacute","ú"},{"udieresis","ü"}
        };

        static string NomeGlifo(string n)
        {
            string s;
            if (GLIFOS.TryGetValue(n, out s)) return s;
            if (n.Length == 1) return n;
            var m = Regex.Match(n, @"^uni([0-9A-Fa-f]{4})");
            if (m.Success) return ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString();
            m = Regex.Match(n, @"^u([0-9A-Fa-f]{4,6})$");
            if (m.Success) return char.ConvertFromUtf32(Convert.ToInt32(m.Groups[1].Value, 16));
            return null;
        }

        // ---------- conteúdo da página ----------
        struct Matriz
        {
            public double A, B, C, D, E, F;
            public static Matriz Id { get { return new Matriz { A = 1, D = 1 }; } }
            public static Matriz Mult(Matriz m, Matriz n) // m × n
            {
                return new Matriz
                {
                    A = m.A * n.A + m.B * n.C, B = m.A * n.B + m.B * n.D,
                    C = m.C * n.A + m.D * n.C, D = m.C * n.B + m.D * n.D,
                    E = m.E * n.A + m.F * n.C + n.E, F = m.E * n.B + m.F * n.D + n.F
                };
            }
        }

        List<ItemTexto> ExtrairItens(PdfDict pagina)
        {
            var itens = new List<ItemTexto>();
            var rec = DictDe(Valor(pagina, "Resources"));
            var conteudo = new List<byte>();
            object c = Resolver(Valor(pagina, "Contents"));
            var partes = c is List<object> ? ((List<object>)c).Select(Resolver).ToList() : new List<object> { c };
            foreach (var p in partes)
            {
                var st = p as PdfStream;
                if (st != null) { conteudo.AddRange(Decodificar(st)); conteudo.Add(10); }
            }
            Executar(conteudo.ToArray(), rec, Matriz.Id, itens, 0);
            return itens;
        }

        void Executar(byte[] conteudo, PdfDict recursos, Matriz ctmInicial, List<ItemTexto> itens, int prof)
        {
            if (prof > 8) return;
            var fontes = DictDe(Valor(recursos, "Font"));
            var xobjs = DictDe(Valor(recursos, "XObject"));

            var pilha = new Stack<Matriz>();
            Matriz ctm = ctmInicial, tm = Matriz.Id, tlm = Matriz.Id;
            FontePdf fonte = new FontePdf();
            double tfs = 10, tc = 0, tw = 0, th = 1, tl = 0, trise = 0;
            var ops = new List<object>();
            var lx = new PdfLexer(conteudo, 0);

            Action<byte[]> mostrar = bytes =>
            {
                var glifos = fonte.Decodificar(bytes);
                var sb = new StringBuilder();
                var inicio = Matriz.Mult(new Matriz { A = 1, D = 1, F = trise }, Matriz.Mult(tm, ctm));
                double escala = Math.Sqrt(inicio.C * inicio.C + inicio.D * inicio.D);
                foreach (var g in glifos)
                {
                    sb.Append(g.Value);
                    double w0 = fonte.Largura(g.Key) / 1000.0;
                    double adv = (w0 * tfs + tc + (g.Key == 32 && !fonte.DoisBytes ? tw : 0)) * th;
                    tm = Matriz.Mult(new Matriz { A = 1, D = 1, E = adv }, tm);
                }
                var fim = Matriz.Mult(tm, ctm);
                string texto = sb.ToString();
                if (texto.Length == 0) return;
                itens.Add(new ItemTexto { X = inicio.E, Y = inicio.F, XFim = fim.E, Tamanho = Math.Max(1, tfs * escala), Texto = texto });
            };

            while (!lx.Fim)
            {
                object t;
                try { t = lx.Ler(); } catch { break; }
                var op = t as PdfOperador;
                if (op == null) { ops.Add(t); continue; }
                string o = op.Nome;
                try
                {
                    switch (o)
                    {
                        case "q": pilha.Push(ctm); break;
                        case "Q": if (pilha.Count > 0) ctm = pilha.Pop(); break;
                        case "cm": if (ops.Count >= 6) ctm = Matriz.Mult(M(ops), ctm); break;
                        case "BT": tm = tlm = Matriz.Id; break;
                        case "Tf":
                            if (ops.Count >= 2)
                            {
                                var nome = ops[ops.Count - 2] as PdfNome;
                                tfs = Numero(ops[ops.Count - 1]);
                                fonte = CarregarFonte(nome != null ? DictDe(Valor(fontes, nome.Valor)) : null);
                            }
                            break;
                        case "Tc": tc = N(ops, 1); break;
                        case "Tw": tw = N(ops, 1); break;
                        case "Tz": th = N(ops, 1) / 100.0; break;
                        case "TL": tl = N(ops, 1); break;
                        case "Ts": trise = N(ops, 1); break;
                        case "Td": tlm = Matriz.Mult(new Matriz { A = 1, D = 1, E = N(ops, 2), F = N(ops, 1) }, tlm); tm = tlm; break;
                        case "TD": tl = -N(ops, 1); tlm = Matriz.Mult(new Matriz { A = 1, D = 1, E = N(ops, 2), F = N(ops, 1) }, tlm); tm = tlm; break;
                        case "Tm": if (ops.Count >= 6) { tlm = M(ops); tm = tlm; } break;
                        case "T*": tlm = Matriz.Mult(new Matriz { A = 1, D = 1, F = -tl }, tlm); tm = tlm; break;
                        case "Tj": if (ops.Count > 0 && ops[ops.Count - 1] is PdfTextoBytes) mostrar(((PdfTextoBytes)ops[ops.Count - 1]).Bytes); break;
                        case "'":
                            tlm = Matriz.Mult(new Matriz { A = 1, D = 1, F = -tl }, tlm); tm = tlm;
                            if (ops.Count > 0 && ops[ops.Count - 1] is PdfTextoBytes) mostrar(((PdfTextoBytes)ops[ops.Count - 1]).Bytes);
                            break;
                        case "\"":
                            if (ops.Count >= 3) { tw = Numero(ops[ops.Count - 3]); tc = Numero(ops[ops.Count - 2]); }
                            tlm = Matriz.Mult(new Matriz { A = 1, D = 1, F = -tl }, tlm); tm = tlm;
                            if (ops.Count > 0 && ops[ops.Count - 1] is PdfTextoBytes) mostrar(((PdfTextoBytes)ops[ops.Count - 1]).Bytes);
                            break;
                        case "TJ":
                            {
                                var arr = ops.Count > 0 ? ops[ops.Count - 1] as List<object> : null;
                                if (arr == null) break;
                                // junta as partes do TJ num item só; espaço grande vira espaço de verdade
                                var inicio = Matriz.Mult(tm, ctm);
                                int antes = itens.Count;
                                var sb = new StringBuilder();
                                foreach (var el in arr)
                                {
                                    if (el is PdfTextoBytes)
                                    {
                                        mostrar(((PdfTextoBytes)el).Bytes);
                                        if (itens.Count > antes) { sb.Append(itens[itens.Count - 1].Texto); itens.RemoveAt(itens.Count - 1); }
                                    }
                                    else if (el is double)
                                    {
                                        double n = (double)el;
                                        tm = Matriz.Mult(new Matriz { A = 1, D = 1, E = -n / 1000.0 * tfs * th }, tm);
                                        if (-n > 180 && sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
                                    }
                                }
                                if (sb.Length > 0)
                                {
                                    double escala = Math.Sqrt(inicio.C * inicio.C + inicio.D * inicio.D);
                                    itens.Add(new ItemTexto { X = inicio.E, Y = inicio.F, XFim = Matriz.Mult(tm, ctm).E, Tamanho = Math.Max(1, tfs * escala), Texto = sb.ToString() });
                                }
                            }
                            break;
                        case "Do":
                            {
                                var nome = ops.Count > 0 ? ops[ops.Count - 1] as PdfNome : null;
                                var xo = nome != null ? Resolver(Valor(xobjs, nome.Valor)) as PdfStream : null;
                                if (xo != null && EhNome(Valor(xo.Dict, "Subtype"), "Form"))
                                {
                                    var mat = Resolver(Valor(xo.Dict, "Matrix")) as List<object>;
                                    var m2 = mat != null && mat.Count == 6 ? M(mat) : Matriz.Id;
                                    var rec2 = DictDe(Valor(xo.Dict, "Resources")) ?? recursos;
                                    Executar(Decodificar(xo), rec2, Matriz.Mult(m2, ctm), itens, prof + 1);
                                }
                            }
                            break;
                        case "BI": // imagem embutida: pula até EI
                            {
                                int p = IndiceDe(conteudo, "ID", lx.P);
                                if (p < 0) { lx.P = conteudo.Length; break; }
                                int q = p + 3;
                                while (q < conteudo.Length - 2 && !(PdfLexer.Espaco(conteudo[q - 1]) && conteudo[q] == 'E' && conteudo[q + 1] == 'I' && (q + 2 >= conteudo.Length || PdfLexer.Espaco(conteudo[q + 2])))) q++;
                                lx.P = q + 2;
                            }
                            break;
                    }
                }
                catch { }
                ops.Clear();
            }
        }

        static double N(List<object> ops, int deTras) { return ops.Count >= deTras ? Numero(ops[ops.Count - deTras]) : 0; }

        static Matriz M(List<object> ops)
        {
            int i = ops.Count - 6;
            return new Matriz { A = Numero(ops[i]), B = Numero(ops[i + 1]), C = Numero(ops[i + 2]), D = Numero(ops[i + 3]), E = Numero(ops[i + 4]), F = Numero(ops[i + 5]) };
        }

        // ---------- itens soltos -> linhas e campos ----------
        static List<LinhaPdf> MontarLinhas(List<ItemTexto> itens, int pagina)
        {
            var linhas = new List<LinhaPdf>();
            var validos = itens.Where(i => i.Texto.Trim().Length > 0).OrderByDescending(i => i.Y).ThenBy(i => i.X).ToList();
            var grupos = new List<List<ItemTexto>>();
            foreach (var it in validos)
            {
                var g = grupos.LastOrDefault();
                if (g != null && Math.Abs(g[0].Y - it.Y) <= Math.Max(2.0, 0.45 * Math.Min(g[0].Tamanho, it.Tamanho))) g.Add(it);
                else grupos.Add(new List<ItemTexto> { it });
            }
            foreach (var g in grupos)
            {
                var ordenados = g.OrderBy(i => i.X).ToList();
                var linha = new LinhaPdf { Pagina = pagina };
                var atual = new StringBuilder();
                ItemTexto ant = null;
                foreach (var it in ordenados)
                {
                    if (ant != null)
                    {
                        double gap = it.X - ant.XFim;
                        double t = Math.Max(ant.Tamanho, it.Tamanho);
                        if (gap > 1.2 * t) { FecharCampo(linha, atual); }
                        else if (gap > 0.12 * t && atual.Length > 0 && atual[atual.Length - 1] != ' ' && !it.Texto.StartsWith(" ")) atual.Append(' ');
                    }
                    atual.Append(it.Texto);
                    ant = ant == null || it.XFim > ant.XFim ? it : ant;
                }
                FecharCampo(linha, atual);
                if (linha.Campos.Count > 0) linhas.Add(linha);
            }
            return linhas;
        }

        static void FecharCampo(LinhaPdf linha, StringBuilder sb)
        {
            string s = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
            if (s.Length > 0) linha.Campos.Add(s);
            sb.Clear();
        }
    }
}
