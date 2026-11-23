// =====================================================================
//  Janela.cs  -  Interface do Painel de Pedidos
//   - Abrir (ou arrastar) o PDF do "Resumo por Vendedor [Pedido]"
//   - Se o arquivo for substituído/salvo de novo, relê sozinho
//   - Abas: Visão geral | Clientes | Pedidos | Conferência
// =====================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PainelPedidos
{
    static class Programa
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT) try { SetProcessDPIAware(); } catch { }
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => MostrarFalha(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => MostrarFalha(e.ExceptionObject as Exception);
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new JanelaPrincipal(args.Length > 0 ? args[0] : null));
            }
            catch (Exception ex) { MostrarFalha(ex); }
        }

        static void MostrarFalha(Exception ex)
        {
            string texto = "Data: " + DateTime.Now + "\r\nWindows: " + Environment.OSVersion + "\r\n.NET: " + Environment.Version +
                           "\r\n\r\n" + (ex == null ? "(erro desconhecido)" : ex.ToString());
            string log = null;
            foreach (var pasta in new[] { AppDomain.CurrentDomain.BaseDirectory, Path.GetTempPath() })
            {
                try { log = Path.Combine(pasta, "PainelPedidos_erro.txt"); File.WriteAllText(log, texto); break; }
                catch { log = null; }
            }
            try
            {
                MessageBox.Show("O Painel de Pedidos encontrou um erro:\n\n" +
                                (ex == null ? "(erro desconhecido)" : ex.GetType().Name + ": " + ex.Message) +
                                (log != null ? "\n\nDetalhes salvos em:\n" + log : ""),
                                "Painel de Pedidos", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------
    static class Tema
    {
        public static readonly CultureInfo BR = new CultureInfo("pt-BR");
        public static readonly Color Fundo = Cor("#F3F3F1"), Cartao = Cor("#FFFFFF"), Borda = Cor("#E2E1DC"), Grade = Cor("#ECEBE7");
        public static readonly Color Texto = Cor("#0B0B0B"), TextoSec = Cor("#52514E"), TextoFraco = Cor("#8A8984");
        public static readonly Color Bom = Cor("#0E7A28"), Ruim = Cor("#C42B2B"), Destaque = Cor("#2A78D6"), DestaqueClaro = Cor("#E8F0FB");
        public static Color Cor(string hex) { return ColorTranslator.FromHtml(hex); }

        // Mesmas cores do Painel de Vendas: Alfa azul, Beta laranja, Gama verde
        public static int OrdemEmpresa(string nome)
        {
            string n = Resumo.Normalizar(nome);
            if (n.Contains("ALFA")) return 0;
            if (n.Contains("BETA")) return 1;
            if (n.Contains("GAMA")) return 2;
            return 3;
        }
        static readonly Color[] CORES = { Cor("#2A78D6"), Cor("#EB6834"), Cor("#1BAF7A"), Cor("#6250D6"), Cor("#E87BA4"), Cor("#EDA100") };
        public static Color CorEmpresa(string nome, int indice)
        {
            int o = OrdemEmpresa(nome);
            return o < 3 ? CORES[o] : CORES[3 + indice % 3];
        }
        public static string Reais(double v) { return (Math.Abs(v) < 0.005 ? 0 : v).ToString("C2", BR); }
        public static string Qtd(double v) { return v.ToString("N3", BR); }
        public static string ReaisCurto(double v)
        {
            double a = Math.Abs(v);
            if (a >= 1000000) return "R$ " + (v / 1000000).ToString("0.##", BR) + " mi";
            if (a >= 10000) return "R$ " + (v / 1000).ToString("0", BR) + " mil";
            return Reais(v);
        }
        public static Font Fonte(float t, bool negrito = false) { return new Font(negrito ? "Segoe UI Semibold" : "Segoe UI", t); }

        public static void EstilizarGrade(DataGridView g)
        {
            g.ReadOnly = true; g.AllowUserToAddRows = false; g.AllowUserToDeleteRows = false; g.AllowUserToResizeRows = false;
            g.RowHeadersVisible = false; g.BackgroundColor = Cartao; g.BorderStyle = BorderStyle.FixedSingle; g.GridColor = Grade;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.MultiSelect = false; g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersDefaultCellStyle.BackColor = Cor("#F7F7F5"); g.ColumnHeadersDefaultCellStyle.ForeColor = Texto;
            g.ColumnHeadersDefaultCellStyle.Font = Fonte(9.75f, true); g.ColumnHeadersHeight = 34;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.RowTemplate.Height = 27; g.DefaultCellStyle.SelectionBackColor = DestaqueClaro; g.DefaultCellStyle.SelectionForeColor = Texto;
            g.Font = Fonte(9.5f);
        }

        public static DataGridViewColumn Coluna(DataGridView g, string titulo, float peso, bool direita = false)
        {
            int i = g.Columns.Add("c" + g.Columns.Count, titulo);
            var c = g.Columns[i];
            c.FillWeight = peso;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            if (direita) { c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight; }
            return c;
        }
    }

    // ------------------------------------------------------------------
    // Configuração: uma PASTA (lê o PDF mais recente de cada empresa)
    // ou uma lista de ARQUIVOS escolhidos à mão.
    // ------------------------------------------------------------------
    // De onde vêm os PDFs de UMA área (Vendas ou Faturamento):
    // uma PASTA (usa o PDF mais recente de cada empresa) ou ARQUIVOS escolhidos à mão.
    class OrigemPdf
    {
        public string Pasta = "";
        public List<string> Arquivos = new List<string>();
        public bool ModoPasta { get { return Pasta != ""; } }
        public bool Vazio { get { return Pasta == "" && Arquivos.Count == 0; } }
    }

    class Configuracao
    {
        public readonly OrigemPdf Vendas = new OrigemPdf(), Faturamento = new OrigemPdf();
        public int Intervalo = 5;
        public string Area = "vendas";          // última área aberta
        static string PastaPrograma { get { return Path.GetDirectoryName(Application.ExecutablePath); } }
        readonly string ini = Path.Combine(PastaPrograma, "PainelPedidos.ini");

        public Configuracao()
        {
            if (!File.Exists(ini)) return;
            foreach (var bruta in File.ReadAllLines(ini))
            {
                string l = bruta.Trim();
                if (l.StartsWith(";") || !l.Contains("=")) continue;
                string k = l.Substring(0, l.IndexOf('=')).Trim().ToLowerInvariant(), v = l.Substring(l.IndexOf('=') + 1).Trim().Trim('"');
                int n;
                var o = k.EndsWith("_faturamento") ? Faturamento : Vendas;
                k = k.Replace("_faturamento", "");
                if (k == "pasta") o.Pasta = v;
                if ((k == "arquivo" || k == "arquivos") && v != "")
                    foreach (var a in v.Split('|')) if (a.Trim() != "") o.Arquivos.Add(a.Trim());
                if (k == "intervalo" && int.TryParse(v, out n)) Intervalo = Math.Max(2, n);
                if (k == "area") Area = v.ToLowerInvariant();
            }
        }

        public static string Completo(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            return Path.IsPathRooted(p) ? p : Path.Combine(PastaPrograma, p);
        }

        public void Salvar()
        {
            try
            {
                File.WriteAllLines(ini, new[] {
                    "; Configuração do Painel de Pedidos",
                    "; pasta    = pasta onde ficam os PDFs do ERP. O painel usa o PDF MAIS RECENTE de cada empresa.",
                    "; arquivos = (se não usar pasta) PDFs escolhidos à mão, separados por |",
                    "",
                    "; --- VENDAS (Resumo por Vendedor [Pedido]) ---",
                    "pasta=" + Vendas.Pasta,
                    "arquivos=" + string.Join(" | ", Vendas.Arquivos), "",
                    "; --- FATURAMENTO (Vendas por Vendedor - Sobre Faturamento) ---",
                    "pasta_faturamento=" + Faturamento.Pasta,
                    "arquivos_faturamento=" + string.Join(" | ", Faturamento.Arquivos), "",
                    "; De quantos em quantos segundos confere se chegou PDF novo",
                    "intervalo=" + Intervalo, "",
                    "; Área aberta por último (vendas ou faturamento)",
                    "area=" + Area });
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------
    // Janela: topo (título, abas Painel | Conferência, selo, botões)
    //         + a tela Painel  ou  a tela Conferência
    // ------------------------------------------------------------------
    class JanelaPrincipal : Form
    {
        readonly Configuracao config = new Configuracao();
        readonly AreaDados vendas, faturamento;

        public JanelaPrincipal(string arquivoInicial)
        {
            Text = "Painel de Pedidos por Vendedor";
            Font = Tema.Fonte(9.75f);
            BackColor = Tema.Fundo;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1600, 900);
            MinimumSize = new Size(1366, 720);
            WindowState = FormWindowState.Maximized;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            vendas = new AreaDados(config, config.Vendas, false) { Dock = DockStyle.Fill };
            faturamento = new AreaDados(config, config.Faturamento, true) { Dock = DockStyle.Fill };
            Controls.Add(vendas); Controls.Add(faturamento);
            vendas.Outra = faturamento; faturamento.Outra = vendas;   // exportar leva as duas áreas
            vendas.TrocarArea += delegate { Mostrar(true); };
            faturamento.TrocarArea += delegate { Mostrar(false); };
            // a lista de licitação é a mesma nas duas áreas
            vendas.LicitacoesMudaram += delegate { faturamento.Reaplicar(); };
            faturamento.LicitacoesMudaram += delegate { vendas.Reaplicar(); };
            vendas.SalaMudou += delegate { faturamento.AtualizarSala(); };
            faturamento.SalaMudou += delegate { vendas.AtualizarSala(); };
            Mostrar(config.Area == "faturamento");

            AllowDrop = true;
            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) =>
            {
                var f = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (f != null && f.Length > 0) (vendas.Visible ? vendas : faturamento).Soltar(f);
            };
            Shown += delegate
            {
                vendas.Iniciar(arquivoInicial);
                faturamento.Iniciar(null);
            };
        }

        void Mostrar(bool fat)
        {
            SuspendLayout();
            faturamento.Visible = fat;
            vendas.Visible = !fat;
            ResumeLayout();
            string area = fat ? "faturamento" : "vendas";
            if (config.Area != area) { config.Area = area; config.Salvar(); }
        }
    }

    // "Vendas   Faturamento" no canto do topo: a área ativa fica escura e sublinhada
    class SeletorArea : Control
    {
        readonly bool faturamento;
        public event EventHandler Trocar;
        static readonly string[] NOMES = { "Vendas", "Faturamento" };
        readonly Font fonte = Tema.Fonte(16f, true);
        Rectangle[] alvos = new Rectangle[2];
        public SeletorArea(bool faturamento)
        {
            this.faturamento = faturamento;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Tema.Cartao;
            int x = 6;
            for (int i = 0; i < 2; i++)
            {
                int w = TextRenderer.MeasureText(NOMES[i], fonte).Width + 8;
                alvos[i] = new Rectangle(x, 0, w, 42);
                x += w + 14;
            }
            Size = new Size(x, 44);
        }
        int Ativo { get { return faturamento ? 1 : 0; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < 2; i++)
            {
                bool at = i == Ativo;
                TextRenderer.DrawText(e.Graphics, NOMES[i], fonte, new Rectangle(alvos[i].X, 4, alvos[i].Width, 32),
                                      at ? Tema.Texto : Tema.Cor("#A6ABB5"), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                if (at) using (var b = new SolidBrush(Tema.Destaque)) e.Graphics.FillRectangle(b, alvos[i].X + 4, 37, alvos[i].Width - 8, 3);
            }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = alvos[1 - Ativo].Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (alvos[1 - Ativo].Contains(e.Location) && Trocar != null) Trocar(this, EventArgs.Empty);
        }
    }

    // ------------------------------------------------------------------
    // Uma ÁREA (Vendas ou Faturamento): cada uma com seus PDFs,
    // seu Painel, sua Conferência e seu Exportar.
    // ------------------------------------------------------------------
    class AreaDados : UserControl
    {
        readonly Configuracao config;
        readonly OrigemPdf origem;
        readonly bool ehFaturamento;
        public event EventHandler TrocarArea, LicitacoesMudaram;
        public AreaDados Outra;
        public bool EhFaturamento { get { return ehFaturamento; } }

        // O que esta área manda para o arquivo exportado (null = sem PDFs lidos)
        public Exportador.Area ParaExportar()
        {
            if (empresas.Count == 0) return null;
            var area = new Exportador.Area { Titulo = Titulo, Relatorio = Relatorio.Trim('"'), Notas = ehFaturamento, Empresas = empresas, Anteriores = anteriores };
            // TODOS os períodos que o programa está lendo (no navegador escolhe qual ver)
            var pers = Periodos(lidos);
            var rg = Licitacoes.Carregar();
            foreach (var p in pers)
            {
                List<Resumo> at, an;
                Selecionar(lidos, p, out at, out an);
                if (at.Count == 0) continue;
                if (p == (periodoEscolhido ?? pers[0])) area.PeriodoInicial = area.Periodos.Count;
                area.Periodos.Add(new Exportador.PeriodoExp { Nome = p,
                    Empresas = at.Select(r => Licitacoes.Aplicar(r, rg)).ToList(),
                    Anteriores = an.Select(r => Licitacoes.Aplicar(r, rg)).ToList() });
            }
            return area;
        }
        string Titulo { get { return ehFaturamento ? "Painel de Faturamento" : "Painel de Vendas"; } }
        string Relatorio { get { return ehFaturamento ? "\"Vendas por Vendedor - Sobre Faturamento\"" : "\"Resumo por Vendedor [Pedido]\""; } }
        readonly Timer vigia = new Timer();
        List<Resumo> brutas = new List<Resumo>(), anterioresBrutas = new List<Resumo>();
        List<Resumo> empresas = new List<Resumo>(), anteriores = new List<Resumo>();   // com licitações marcadas
        List<RegraLicitacao> regras = Licitacoes.Carregar();
        string assinatura;
        bool lendo;
        int nArquivos;
        readonly Dictionary<string, KeyValuePair<DateTime, Resumo>> cache = new Dictionary<string, KeyValuePair<DateTime, Resumo>>();
        static readonly Color COR_LICITACAO = Tema.Cor("#6250D6");

        readonly Label lblSub = new Label(), lblAviso = new Label(), lblAlerta = new Label(), selo = new Label();
        readonly List<Button> abas = new List<Button>();
        readonly TelaPainel painel = new TelaPainel();
        // ---- SALA DE VENDAS: o mesmo painel, só com os vendedores escolhidos ----
        readonly TelaPainel painelSala = new TelaPainel { Restrito = new HashSet<string>(), RotuloTotal = "Total da sala de vendas" };
        readonly Panel telaSala = new Panel { Dock = DockStyle.Fill };
        readonly Label lblSala = new Label();
        public event EventHandler SalaMudou;
        Action arrumarTopo = delegate { };

        // a área escondida não se arruma sozinha: ao aparecer, reposiciona o topo
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) { arrumarTopo(); cmbPeriodo.Invalidate(); lblPeriodo.Invalidate(); }
        }
        readonly Panel telaConf = new Panel();
        // Conferência
        readonly Label lblConf = new Label();
        readonly DataGridView gradeConf = new DataGridView();
        readonly TextBox txtLido = new TextBox();

        public AreaDados(Configuracao config, OrigemPdf origem, bool faturamento)
        {
            this.config = config; this.origem = origem; ehFaturamento = faturamento;
            painel.Notas = faturamento;
            BackColor = Tema.Fundo;
            MontarTopo();
            MontarConferencia();
            painel.Dock = DockStyle.Fill;
            painel.PedirLicitacao += (cliente, vend) => MarcarLicitacao(cliente, vend);
            painel.PedirListaLicitacao += delegate { GerenciarLicitacoes(); };
            Controls.Add(painel); painel.BringToFront();
            Controls.Add(telaConf); telaConf.BringToFront();
            MontarSala();
            Controls.Add(telaSala); telaSala.BringToFront();
            Mostrar(0);
            vigia.Interval = config.Intervalo * 1000;
            vigia.Tick += delegate { Verificar(false); };
        }

        // Chamado quando a janela aparece. Cria o handle mesmo se a área estiver escondida
        // (a leitura em segundo plano devolve o resultado com BeginInvoke).
        public void Iniciar(string arquivoInicial)
        {
            if (!IsHandleCreated) CreateControl();
            var h = Handle;
            if (!string.IsNullOrEmpty(arquivoInicial)) AdicionarArquivos(new[] { arquivoInicial }, true);
            else Verificar(true);
            vigia.Start();
        }

        public void Soltar(string[] f)
        {
            if (f.Length == 1 && Directory.Exists(f[0])) UsarPasta(f[0]);
            else AdicionarArquivos(f.Where(File.Exists).ToArray(), false);
        }

        public void Reaplicar()
        {
            regras = Licitacoes.Carregar();
            empresas = brutas.Select(r => Licitacoes.Aplicar(r, regras)).ToList();
            anteriores = anterioresBrutas.Select(r => Licitacoes.Aplicar(r, regras)).ToList();
            AtualizarTudo();
        }

        // ---------------- topo ----------------
        void MontarTopo()
        {
            var topo = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Tema.Cartao };
            topo.Paint += (s, e) => { using (var p = new Pen(Tema.Borda)) e.Graphics.DrawLine(p, 0, topo.Height - 1, topo.Width, topo.Height - 1); };
            // Vendas | Faturamento: troca de área (cada uma tem os seus PDFs)
            var titulo = new SeletorArea(ehFaturamento) { Location = new Point(14, 4) };
            titulo.Trocar += delegate { if (TrocarArea != null) TrocarArea(this, EventArgs.Empty); };
            lblSub.AutoSize = false; lblSub.AutoEllipsis = true; lblSub.ForeColor = Tema.TextoSec; lblSub.Location = new Point(22, 48); lblSub.Height = 22;
            lblSub.Text = "Nenhum PDF carregado: clique em \"Escolher pasta dos PDFs...\" e escolha a pasta onde você salva o relatório " + Relatorio + ".";
            topo.Controls.Add(titulo); topo.Controls.Add(lblSub);

            foreach (var nome in new[] { "Painel", "Sala de Vendas", "Conferência" })
            {
                var b = new Button { Text = nome, FlatStyle = FlatStyle.Flat, Size = new Size(nome == "Sala de Vendas" ? 124 : (nome == "Painel" ? 70 : 104), 34), Cursor = Cursors.Hand, BackColor = Tema.Cartao };
                b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = Tema.Cartao; b.FlatAppearance.MouseDownBackColor = Tema.Cartao;
                // aba = texto simples; a ativa ganha um sublinhado
                b.Paint += (s2, e2) => { var bb = (Button)s2; if (bb.Font.Bold || bb.ForeColor == Tema.Texto) using (var br = new SolidBrush(Tema.Texto)) e2.Graphics.FillRectangle(br, 10, bb.Height - 3, bb.Width - 20, 2); };
                int indice = abas.Count;
                b.Click += delegate { Mostrar(indice); };
                abas.Add(b); topo.Controls.Add(b);
            }
            // selo da conferência: sempre visível; clicando abre a aba
            selo.AutoSize = true; selo.Padding = new Padding(10, 5, 10, 5); selo.Font = Tema.Fonte(9.25f, true); selo.Cursor = Cursors.Hand; selo.Visible = false;
            selo.Click += delegate { Mostrar(2); };
            topo.Controls.Add(selo);

            var btnPasta = new Button { Text = "Escolher pasta dos PDFs...", AutoSize = true, FlatStyle = FlatStyle.System };
            btnPasta.Click += delegate { EscolherPasta(); };
            var btnAbrir = new Button { Text = "Abrir PDFs...", AutoSize = true, FlatStyle = FlatStyle.System };
            btnAbrir.Click += delegate { EscolherArquivos(); };
            var btnAtualizar = new Button { Text = "Atualizar", AutoSize = true, FlatStyle = FlatStyle.System };
            btnAtualizar.Click += delegate { Verificar(true); };
            var btnExportar = new Button { Text = "Exportar para enviar...", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Tema.Destaque, ForeColor = Color.White, Font = Tema.Fonte(9.75f, true), Cursor = Cursors.Hand };
            btnExportar.FlatAppearance.BorderSize = 0; btnExportar.Padding = new Padding(6, 2, 6, 2);
            btnExportar.Click += delegate { Exportar(); };
            // escolher qual período ver (aparece quando há PDFs de mais de um período)
            lblPeriodo.Text = "Ver o período:"; lblPeriodo.AutoSize = true; lblPeriodo.ForeColor = Tema.TextoSec; lblPeriodo.Visible = false;
            cmbPeriodo.DropDownStyle = ComboBoxStyle.DropDownList; cmbPeriodo.Width = 300; cmbPeriodo.Visible = false; cmbPeriodo.Font = Tema.Fonte(9.75f, true);
            cmbPeriodo.SelectedIndexChanged += delegate { EscolherPeriodo(); };
            topo.Controls.AddRange(new Control[] { btnPasta, btnAbrir, btnAtualizar, btnExportar, lblPeriodo, cmbPeriodo });
            arrumarTopo = delegate
            {
                int ax = titulo.Right + 28;
                foreach (var b in abas) { b.Location = new Point(ax, 8); ax = b.Right + 2; }
                selo.Location = new Point(ax + 14, 16);
                btnExportar.Location = new Point(topo.Width - btnExportar.Width - 20, 12);
                btnAtualizar.Location = new Point(btnExportar.Left - btnAtualizar.Width - 8, 12);
                btnAbrir.Location = new Point(btnAtualizar.Left - btnAbrir.Width - 8, 12);
                btnPasta.Location = new Point(btnAbrir.Left - btnPasta.Width - 8, 12);
                cmbPeriodo.Location = new Point(topo.Width - cmbPeriodo.Width - 20, 45);
                lblPeriodo.Location = new Point(cmbPeriodo.Left - lblPeriodo.PreferredWidth - 6, 49);
                lblSub.Width = (cmbPeriodo.Visible ? lblPeriodo.Left - 16 : topo.Width - 20) - lblSub.Left;
            };
            topo.Resize += delegate { arrumarTopo(); };

            lblAviso.Dock = DockStyle.Top; lblAviso.Height = 30; lblAviso.TextAlign = ContentAlignment.MiddleCenter;
            lblAviso.BackColor = Tema.Cor("#E3F1E6"); lblAviso.ForeColor = Tema.Bom; lblAviso.Font = Tema.Fonte(9.75f, true); lblAviso.Visible = false;
            lblAlerta.Dock = DockStyle.Top; lblAlerta.Height = 32; lblAlerta.TextAlign = ContentAlignment.MiddleLeft; lblAlerta.Padding = new Padding(20, 0, 0, 0);
            lblAlerta.BackColor = Tema.Cor("#FFF3D6"); lblAlerta.ForeColor = Tema.Cor("#7A4B00"); lblAlerta.Font = Tema.Fonte(9.75f, true); lblAlerta.Visible = false;
            Controls.Add(lblAviso);
            Controls.Add(lblAlerta);
            Controls.Add(topo);
        }

        void MontarSala()
        {
            var faixa = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Tema.Fundo, Padding = new Padding(24, 12, 24, 0) };
            var caixa = new Panel { Dock = DockStyle.Fill, BackColor = Tema.Cartao };
            lblSala.Dock = DockStyle.Fill; lblSala.TextAlign = ContentAlignment.MiddleLeft; lblSala.Font = Tema.Fonte(10.5f);
            lblSala.Padding = new Padding(14, 0, 0, 0); lblSala.AutoEllipsis = true;
            var btn = new Button { Text = "Escolher vendedores da sala...", AutoSize = true, FlatStyle = FlatStyle.System, Dock = DockStyle.Right };
            btn.Click += delegate { EscolherSala(); };
            var marg = new Panel { Dock = DockStyle.Right, Width = 8 };
            caixa.Controls.Add(lblSala); caixa.Controls.Add(btn); caixa.Controls.Add(marg);
            faixa.Controls.Add(caixa);
            painelSala.Dock = DockStyle.Fill;
            painelSala.Notas = ehFaturamento;
            painelSala.PedirLicitacao += (cliente, vend) => MarcarLicitacao(cliente, vend);
            painelSala.PedirListaLicitacao += delegate { GerenciarLicitacoes(); };
            telaSala.Controls.Add(painelSala); telaSala.Controls.Add(faixa);
        }

        // Relê a lista (pode ter mudado na outra área) e redesenha a Sala de Vendas
        public void AtualizarSala()
        {
            var nomes = SalaVendas.Carregar();
            painelSala.Restrito = new HashSet<string>(nomes.Select(Resumo.Normalizar));
            painelSala.Definir(empresas, anteriores);
            double sala = painelSala.Todos.Sum(p => p.Valor), geral = painelSala.TotalSemRestricao;
            if (nomes.Count == 0)
                lblSala.Text = "SALA DE VENDAS:  nenhum vendedor escolhido ainda  →  clique em \"Escolher vendedores da sala...\"";
            else
                lblSala.Text = "SALA DE VENDAS  (" + nomes.Count + " vendedores):  " + string.Join(", ", nomes.OrderBy(n => n)) +
                               "      →   " + Tema.Reais(sala) + (geral > 0 ? "  =  " + (sala / geral * 100).ToString("0.0", Tema.BR) + "% do total das empresas" : "");
        }

        void EscolherSala()
        {
            var atuais = SalaVendas.Carregar();
            // todos os vendedores que aparecem nos PDFs lidos (todos os períodos) + os já escolhidos
            var nomes = lidos.SelectMany(r => r.Vendedores).Where(v => !v.Manual).Select(v => v.Nome)
                             .Concat(Outra != null ? Outra.NomesVendedores() : new List<string>())
                             .Concat(atuais)
                             .GroupBy(Resumo.Normalizar).Select(g => g.First()).OrderBy(n => n).ToList();
            using (var dlg = new Form())
            {
                dlg.Text = "Vendedores da Sala de Vendas"; dlg.Font = Tema.Fonte(9.75f); dlg.BackColor = Tema.Cartao;
                dlg.StartPosition = FormStartPosition.CenterParent; dlg.ClientSize = new Size(440, 520); dlg.ShowInTaskbar = false;
                dlg.MinimizeBox = dlg.MaximizeBox = false; dlg.Padding = new Padding(16);
                var topo = new Label { Dock = DockStyle.Top, Height = 44, ForeColor = Tema.TextoSec,
                    Text = "Marque quem faz parte da sala de vendas. Fica salvo até você mudar\ne vale para Vendas e Faturamento." };
                var lista = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
                var sel = new HashSet<string>(atuais.Select(Resumo.Normalizar));
                foreach (var n in nomes) lista.Items.Add(n, sel.Contains(Resumo.Normalizar(n)));
                var baixo = new Panel { Dock = DockStyle.Bottom, Height = 48 };
                var ok = new Button { Text = "Salvar", AutoSize = true, FlatStyle = FlatStyle.System, DialogResult = DialogResult.OK };
                var canc = new Button { Text = "Cancelar", AutoSize = true, FlatStyle = FlatStyle.System, DialogResult = DialogResult.Cancel };
                var nenhum = new LinkLabel { Text = "Desmarcar todos", AutoSize = true, Location = new Point(0, 18) };
                nenhum.LinkClicked += delegate { for (int k = 0; k < lista.Items.Count; k++) lista.SetItemChecked(k, false); };
                baixo.Controls.AddRange(new Control[] { ok, canc, nenhum });
                baixo.Layout += delegate { canc.Location = new Point(baixo.Width - canc.Width, 12); ok.Location = new Point(canc.Left - ok.Width - 8, 12); };
                dlg.Controls.Add(lista); dlg.Controls.Add(baixo); dlg.Controls.Add(topo);
                dlg.AcceptButton = ok; dlg.CancelButton = canc;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { SalaVendas.Salvar(lista.CheckedItems.Cast<object>().Select(o => o.ToString())); }
                catch (Exception ex) { MessageBox.Show(this, "Não consegui salvar a sala de vendas:\n" + ex.Message, "Sala de Vendas", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            }
            AtualizarSala();
            if (SalaMudou != null) SalaMudou(this, EventArgs.Empty);
        }

        public List<string> NomesVendedores()
        {
            return lidos.SelectMany(r => r.Vendedores).Where(v => !v.Manual).Select(v => v.Nome).ToList();
        }

        void Mostrar(int i)
        {
            painel.Visible = i == 0;
            telaSala.Visible = i == 1;
            telaConf.Visible = i == 2;
            for (int k = 0; k < abas.Count; k++)
            {
                bool ativo = k == i;
                abas[k].BackColor = Tema.Cartao;
                abas[k].ForeColor = ativo ? Tema.Texto : Tema.TextoSec;
                abas[k].Font = Tema.Fonte(10f, ativo);
                abas[k].Invalidate();
            }
            if (i == 2) gradeConf.ClearSelection();
        }

        void AtualizarSelo(int problemas, bool nada)
        {
            selo.Visible = empresas.Count > 0;
            bool ok = problemas == 0 && !nada;
            selo.Text = ok ? "✓ tudo confere" : nada ? (ehFaturamento ? "⚠ Nenhuma nota lida" : "⚠ Nenhum pedido lido") : "⚠ " + problemas + " total(is) não bate(m)";
            selo.BackColor = ok ? Tema.Cartao : Tema.Cor("#FBE6E6");
            selo.Padding = ok ? new Padding(4, 5, 4, 5) : new Padding(10, 5, 10, 5);
            selo.ForeColor = ok ? Tema.Bom : Tema.Ruim;
        }

        // ---------------- conferência ----------------
        void MontarConferencia()
        {
            telaConf.Dock = DockStyle.Fill; telaConf.Padding = new Padding(20, 16, 20, 20); telaConf.BackColor = Tema.Fundo;
            lblConf.Dock = DockStyle.Top; lblConf.Height = 44; lblConf.Font = Tema.Fonte(11f, true); lblConf.TextAlign = ContentAlignment.MiddleLeft;
            lblConf.Padding = new Padding(12, 0, 0, 0);
            var expl = new Label { Dock = DockStyle.Top, Height = 30, ForeColor = Tema.TextoSec, TextAlign = ContentAlignment.MiddleLeft,
                                   Text = "Confere se a leitura dos PDFs está certa: para cada empresa, a soma " + (ehFaturamento ? "das notas lidas" : "dos pedidos lidos") + " tem que bater com o total impresso no relatório." };
            Tema.EstilizarGrade(gradeConf); gradeConf.Dock = DockStyle.Top; gradeConf.Height = 380;
            Tema.Coluna(gradeConf, "Empresa", 40); Tema.Coluna(gradeConf, "Vendedor", 100); Tema.Coluna(gradeConf, ehFaturamento ? "Notas lidas" : "Pedidos lidos", 40, true);
            Tema.Coluna(gradeConf, ehFaturamento ? "Soma das notas" : "Soma dos pedidos", 55, true); Tema.Coluna(gradeConf, "Total no relatório", 55, true);
            Tema.Coluna(gradeConf, "Diferença", 45, true); Tema.Coluna(gradeConf, "Situação", 90);
            var lblTxt = new Label { Text = "Texto lido dos PDFs (para conferir linha a linha)", Dock = DockStyle.Top, Height = 34, TextAlign = ContentAlignment.BottomLeft, Font = Tema.Fonte(10f, true), ForeColor = Tema.Texto };
            txtLido.Multiline = true; txtLido.ReadOnly = true; txtLido.ScrollBars = ScrollBars.Both; txtLido.WordWrap = false;
            txtLido.Dock = DockStyle.Fill; txtLido.Font = new Font("Consolas", 9.5f); txtLido.BackColor = Tema.Cartao;
            telaConf.Controls.Add(txtLido); telaConf.Controls.Add(lblTxt); telaConf.Controls.Add(gradeConf); telaConf.Controls.Add(expl); telaConf.Controls.Add(lblConf);
            gradeConf.VisibleChanged += delegate { gradeConf.ClearSelection(); };
        }

        // ---------------- escolher pasta / arquivos ----------------
        // ---------------- escolher pasta / arquivos ----------------
        void EscolherPasta()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = Titulo.ToUpperInvariant() + ": escolha a pasta onde você salva os PDFs " + Relatorio + ".\n" +
                                  "O painel usa o PDF mais recente de cada empresa que estiver nela.";
                if (origem.Pasta != "") try { dlg.SelectedPath = Configuracao.Completo(origem.Pasta); } catch { }
                if (dlg.ShowDialog(this) == DialogResult.OK) UsarPasta(dlg.SelectedPath);
            }
        }
        void UsarPasta(string pasta)
        {
            origem.Pasta = pasta;
            origem.Arquivos.Clear();
            config.Salvar();
            assinatura = null;
            Verificar(true);
        }
        void EscolherArquivos()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = Titulo + ": escolha o(s) PDF(s) " + Relatorio + " — pode marcar as 3 empresas juntas";
                dlg.Filter = "Resumo do ERP (*.pdf;*.txt;*.csv)|*.pdf;*.txt;*.csv|Todos os arquivos|*.*";
                dlg.Multiselect = true;
                if (dlg.ShowDialog(this) == DialogResult.OK) AdicionarArquivos(dlg.FileNames, true);
            }
        }
        // substituir = true: os arquivos escolhidos passam a ser os únicos.
        // false (arrastar): soma aos que já estão; PDF da mesma empresa troca o antigo.
        void AdicionarArquivos(string[] arquivos, bool substituir)
        {
            if (arquivos.Length == 0) return;
            if (origem.ModoPasta || substituir) origem.Arquivos.Clear();
            origem.Pasta = "";
            foreach (var a in arquivos)
            {
                origem.Arquivos.RemoveAll(x => string.Equals(Configuracao.Completo(x), a, StringComparison.OrdinalIgnoreCase));
                origem.Arquivos.Add(a);
            }
            config.Salvar();
            assinatura = null;
            Verificar(true);
        }
        // ---------------- leitura ----------------
        static bool EhExemplo(string pasta, string f)
        {
            string rel = f.Substring(Math.Min(f.Length, pasta.Length)).ToLowerInvariant();
            return rel.Contains(Path.DirectorySeparatorChar + "exemplo" + Path.DirectorySeparatorChar) || rel.Contains("/exemplo/");
        }

        List<string> ArquivosAtuais()
        {
            if (origem.ModoPasta)
            {
                string pasta = Configuracao.Completo(origem.Pasta);
                if (!Directory.Exists(pasta)) return null;
                // do mais antigo para o mais novo: o mais novo de cada empresa vence
                // inclui as SUBPASTAS (ex.: uma pasta por mês dentro da pasta escolhida)
                // (a pasta "exemplo", com os PDFs FICTÍCIOS de teste, fica de fora)
                return Directory.GetFiles(pasta, "*.pdf", SearchOption.AllDirectories)
                    .Where(f => !EhExemplo(pasta, f))
                    .OrderBy(f => File.GetLastWriteTime(f)).ToList();
            }
            return origem.Arquivos.Select(Configuracao.Completo).Where(File.Exists).ToList();
        }
        // ---------------- leitura ----------------
        void Verificar(bool forcar)
        {
            if (lendo || origem.Vazio) return;
            List<string> arquivos;
            string ass;
            try
            {
                arquivos = ArquivosAtuais();
                if (arquivos == null) { lblSub.Text = "⚠ Pasta não encontrada: " + origem.Pasta; return; }
                ass = string.Join("|", arquivos.Select(f => f + "@" + File.GetLastWriteTime(f).Ticks));
            }
            catch (Exception ex) { lblSub.Text = "⚠ " + ex.Message; return; }
            if (!forcar && ass == assinatura) return;

            bool releitura = assinatura != null && !forcar;
            lendo = true;
            lblSub.Text = "Lendo os PDFs...";
            Task.Factory.StartNew(() => LerTodos(arquivos)).ContinueWith(t => BeginInvoke(new Action(() =>
            {
                lendo = false;
                assinatura = ass;
                if (t.IsFaulted) { lblSub.Text = "⚠ " + t.Exception.GetBaseException().Message; return; }
                nArquivos = arquivos.Count;
                Receber(t.Result);
                if (releitura)
                {
                    lblAviso.Text = "✓  Painel atualizado às " + DateTime.Now.ToString("HH:mm:ss") + " (PDF novo ou alterado)";
                    lblAviso.Visible = true;
                    var apaga = new Timer { Interval = 6000 };
                    apaga.Tick += delegate { lblAviso.Visible = false; apaga.Stop(); apaga.Dispose(); };
                    apaga.Start();
                }
            })));
        }

        class Leitura { public List<Resumo> Lidos; public List<string> Erros; }

        static DateTime? FimDoPeriodo(Resumo r)
        {
            var m = System.Text.RegularExpressions.Regex.Matches(r.Periodo ?? "", @"\d{2}/\d{2}/\d{4}");
            DateTime d;
            if (m.Count > 0 && DateTime.TryParseExact(m[m.Count - 1].Value, "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d)) return d;
            return null;
        }

        // Lê os PDFs (reaproveitando os que não mudaram). Para cada empresa:
        //  - atual    = o PDF mais recente
        //  - anterior = o PDF mais recente com período que TERMINA ANTES do atual (para o ▲▼)
        int outraArea;
        Leitura LerTodos(List<string> arquivos)
        {
            outraArea = 0;
            var erros = new List<string>();
            var lidos = new List<Resumo>();
            foreach (var f in arquivos)
            {
                try
                {
                    DateTime mod = File.GetLastWriteTime(f);
                    KeyValuePair<DateTime, Resumo> c;
                    Resumo r;
                    lock (cache) r = cache.TryGetValue(f, out c) && c.Key == mod ? c.Value : null;
                    if (r == null)
                    {
                        r = Resumo.Carregar(f);
                        lock (cache) cache[f] = new KeyValuePair<DateTime, Resumo>(mod, r);
                    }
                    if (r.Periodo == "" && r.Vendedores.Count == 0 && r.TotalGeralInformado == null)
                    { erros.Add(Path.GetFileName(f) + ": não parece ser um Resumo por Vendedor"); continue; }
                    // relatório da OUTRA área (ex.: faturamento na pasta de vendas): ignora sem reclamar,
                    // assim as duas áreas podem usar a mesma pasta "mãe" com tudo dentro
                    if (r.EhFaturamento != ehFaturamento) { outraArea++; continue; }
                    lidos.Add(r);
                }
                catch (Exception ex) { erros.Add(Path.GetFileName(f) + ": " + ex.Message); }
            }
            return new Leitura { Lidos = lidos, Erros = erros };
        }

        // Escolhe, para cada empresa, o PDF do período mostrado (atual) e o do período anterior (para o ▲▼).
        //  periodo = null  -> o mais recente de cada empresa
        //  periodo = "01/10/2026 a 30/10/2026" -> exatamente esse período (empresa sem ele fica de fora)
        static void Selecionar(List<Resumo> lidos, string periodo, out List<Resumo> atuaisOut, out List<Resumo> anterioresOut)
        {
            var atuais = new List<Resumo>();
            var anteriores = new List<Resumo>();
            // "mais recente" = o período mais recente da pasta, IGUAL para todas as empresas
            // (não mistura: empresa sem PDF desse período fica de fora, com aviso)
            if (periodo == null) periodo = Periodos(lidos).FirstOrDefault();
            foreach (var grupo in lidos.GroupBy(r => r.EmpresaCurta))
            {
                // ordem: pela data escrita no PDF; empate: o arquivo salvo por último
                var ordenado = grupo.Select((r, i) => new { r, i }).OrderBy(x => FimDoPeriodo(x.r) ?? DateTime.MinValue).ThenBy(x => x.i).Select(x => x.r).ToList();
                var atual = periodo == null ? ordenado.Last() : ordenado.LastOrDefault(r => r.Periodo == periodo);
                if (atual == null) continue;
                atuais.Add(atual);
                var fim = FimDoPeriodo(atual);
                if (fim == null) continue;
                var ant = ordenado.Where(r => r != atual && r.Periodo != atual.Periodo && FimDoPeriodo(r).HasValue && FimDoPeriodo(r).Value < fim.Value).LastOrDefault();
                if (ant != null) anteriores.Add(ant);
            }
            Func<Resumo, int> ordem = r => Tema.OrdemEmpresa(r.EmpresaCurta);
            atuaisOut = atuais.OrderBy(ordem).ThenBy(r => r.EmpresaCurta).ToList();
            anterioresOut = anteriores.OrderBy(ordem).ThenBy(r => r.EmpresaCurta).ToList();
        }

        // Todos os períodos lidos, do mais recente para o mais antigo
        static List<string> Periodos(List<Resumo> lidos)
        {
            return lidos.Where(r => r.Periodo != "").GroupBy(r => r.Periodo)
                        .OrderByDescending(g => g.Max(r => FimDoPeriodo(r) ?? DateTime.MinValue)).Select(g => g.Key).ToList();
        }

        List<Resumo> lidos = new List<Resumo>();
        List<string> errosLeitura = new List<string>();
        string periodoEscolhido;          // null = mais recente
        readonly ComboBox cmbPeriodo = new ComboBox();
        readonly ToolTip dicaSelo = new ToolTip { InitialDelay = 200, AutoPopDelay = 15000 };
        readonly Label lblPeriodo = new Label();
        bool montandoPeriodos;

        void Receber(Leitura l)
        {
            lidos = l.Lidos; errosLeitura = l.Erros;
            var pers = Periodos(lidos);
            if (periodoEscolhido != null && !pers.Contains(periodoEscolhido)) periodoEscolhido = null;
            montandoPeriodos = true;
            cmbPeriodo.Items.Clear();
            for (int i = 0; i < pers.Count; i++) cmbPeriodo.Items.Add(i == 0 ? pers[i] + "  (mais recente)" : pers[i]);
            cmbPeriodo.SelectedIndex = pers.Count == 0 ? -1 : periodoEscolhido == null ? 0 : pers.IndexOf(periodoEscolhido);
            cmbPeriodo.Tag = pers;
            montandoPeriodos = false;
            cmbPeriodo.Visible = lblPeriodo.Visible = pers.Count > 1;
            arrumarTopo();
            MostrarPeriodo();
        }

        void MostrarPeriodo()
        {
            List<Resumo> atuais, ants;
            Selecionar(lidos, periodoEscolhido, out atuais, out ants);
            var erros = new List<string>(errosLeitura);
            Aplicar(atuais, ants, erros);
        }

        void EscolherPeriodo()
        {
            if (montandoPeriodos || cmbPeriodo.SelectedIndex < 0) return;
            var pers = (List<string>)cmbPeriodo.Tag;
            periodoEscolhido = cmbPeriodo.SelectedIndex == 0 ? null : pers[cmbPeriodo.SelectedIndex];
            MostrarPeriodo();
        }

        void Aplicar(List<Resumo> atuais, List<Resumo> ants, List<string> erros)
        {
            brutas = atuais; anterioresBrutas = ants;
            regras = Licitacoes.Carregar();   // relê: outra pessoa pode ter mudado
            empresas = brutas.Select(r => Licitacoes.Aplicar(r, regras)).ToList();
            anteriores = anterioresBrutas.Select(r => Licitacoes.Aplicar(r, regras)).ToList();

            var alertas = new List<string>();
            {
                string perVisto = periodoEscolhido ?? Periodos(lidos).FirstOrDefault();
                var faltam = lidos.Select(r => r.EmpresaCurta).Distinct().Where(e => !empresas.Any(x => x.EmpresaCurta == e)).ToList();
                if (faltam.Count > 0) alertas.Add("Falta o PDF de " + perVisto + " de: " + string.Join(", ", faltam) + " (fora da soma; salve o PDF dela na pasta)");
            }
            if (empresas.Count > 1 && empresas.Select(r => r.Periodo).Distinct().Count() > 1)
                alertas.Add("Atenção: os PDFs têm períodos diferentes — " + string.Join("   ·   ", empresas.Select(r => r.EmpresaCurta + " " + (r.Periodo == "" ? "(sem período)" : r.Periodo))));
            var semAnterior = anteriores.Count > 0 ? empresas.Where(e => !anteriores.Any(a => a.EmpresaCurta == e.EmpresaCurta)).Select(e => e.EmpresaCurta).ToList() : new List<string>();
            if (semAnterior.Count > 0) alertas.Add("Sem período anterior para comparar (fica sem ▲▼): " + string.Join(", ", semAnterior));
            if (erros.Count > 0) alertas.Add("Não consegui usar: " + string.Join("; ", erros));
            lblAlerta.Text = "⚠  " + string.Join("     |     ", alertas);
            lblAlerta.Visible = alertas.Count > 0;

            AtualizarTudo();
        }

        void AtualizarTudo()
        {
            painel.Definir(empresas, anteriores);
            AtualizarSala();
            if (empresas.Count == 0)
            {
                lblSub.Text = origem.Vazio ? "Nenhum PDF carregado: clique em \"Escolher pasta dos PDFs...\" e escolha a pasta onde você salva o relatório " + Relatorio + "." : origem.ModoPasta ? "Nenhum relatório " + Relatorio + " encontrado na pasta" : "Nenhum relatório aberto";
                gradeConf.Rows.Clear(); txtLido.Text = ""; selo.Visible = false;
                return;
            }
            var partes = new List<string>();
            var per = empresas.Select(e => e.Periodo).Where(p => p != "").Distinct().ToList();
            if (per.Count > 0) partes.Add("Período " + string.Join(" / ", per));
            if (painel.TemAnterior) partes.Add("▲▼ comparado com " + painel.ComparacaoTexto);
            lblSub.Text = string.Join("   ·   ", partes);
            // o resto (empresas, pasta, hora da leitura) fica no selo, ao passar o mouse
            dicaSelo.SetToolTip(selo, string.Join(" + ", empresas.Select(e => e.EmpresaCurta)) + "\n" +
                (origem.ModoPasta ? "Pasta: " + Path.GetFileName(Configuracao.Completo(origem.Pasta).TrimEnd('\\', '/')) + " (" + lidos.Count + " PDFs desta área" + (outraArea > 0 ? "; " + outraArea + " da outra área ignorados" : "") + ")" : empresas.Count + " PDF(s) abertos") +
                " · lido às " + DateTime.Now.ToString("HH:mm") + "\n" +
                "Períodos encontrados: " + string.Join(" · ", Periodos(lidos).Select(p => p + " (" + lidos.Count(r => r.Periodo == p) + " PDF)")) +
                "\nClique para abrir a Conferência");
            PreencherConferencia(empresas);
        }

        // ---------------- licitação ----------------
        // Marca (ou desmarca) a razão social como licitação. A venda continua com o vendedor do PDF.
        void MarcarLicitacao(string cliente, string vendedorNoPdf)
        {
            regras = Licitacoes.Carregar();
            bool jaMarcado = Licitacoes.EhLicitacao(regras, cliente);
            string msg = jaMarcado
                ? cliente + "\n\nEste cliente já está marcado como LICITAÇÃO.\n\nDesmarcar? As vendas dele voltam a aparecer como venda normal."
                : cliente + "\n\nMarcar este cliente como LICITAÇÃO?\n\n" +
                  "Toda venda para esta razão social (nas 3 empresas, em Vendas e em Faturamento, e nos próximos PDFs) vai aparecer " +
                  "marcada como licitação. Ela continua no vendedor que está no PDF" + (string.IsNullOrEmpty(vendedorNoPdf) ? "" : " (" + vendedorNoPdf + ")") + ".";
            if (MessageBox.Show(this, msg, jaMarcado ? "Desmarcar licitação" : "Marcar como licitação", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            regras.RemoveAll(x => Licitacoes.Bate(x.Chave, Resumo.Normalizar(cliente)));
            if (!jaMarcado) regras.Add(new RegraLicitacao { Cliente = cliente });
            SalvarEReaplicar();
        }
        void GerenciarLicitacoes()
        {
            regras = Licitacoes.Carregar();
            using (var dlg = new DialogoRegras(regras))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                regras = dlg.Regras;
                SalvarEReaplicar();
            }
        }
        void SalvarEReaplicar()
        {
            try { Licitacoes.Salvar(regras); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não consegui salvar a lista em:\n" + Licitacoes.Arquivo + "\n\n" + ex.Message +
                                "\n\nVale até fechar o programa.", "Licitação", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            empresas = brutas.Select(r => Licitacoes.Aplicar(r, regras)).ToList();
            anteriores = anterioresBrutas.Select(r => Licitacoes.Aplicar(r, regras)).ToList();
            AtualizarTudo();
            if (LicitacoesMudaram != null) LicitacoesMudaram(this, EventArgs.Empty);
        }

        // ---------------- exportar para enviar ----------------
        void Exportar()
        {
            // as DUAS áreas vão no mesmo arquivo (Vendas primeiro); quem recebe troca no topo da página
            var porOrdem = ehFaturamento || Outra == null ? new[] { Outra, this } : new[] { this, Outra };
            var areas = new List<Exportador.Area>();
            var fora = new List<string>();
            int inicial = 0;
            foreach (var ar in porOrdem)
            {
                if (ar == null) continue;
                var x = ar.ParaExportar();
                if (x == null) { fora.Add(ar.Titulo); continue; }
                if (ar == this) inicial = areas.Count;
                areas.Add(x);
            }
            if (areas.Count == 0)
            {
                MessageBox.Show(this, "Primeiro escolha a pasta dos PDFs (botão \"Escolher pasta dos PDFs...\").", "Exportar", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // nome do arquivo: do período mais antigo ao mais recente lido
            var todosPer = areas.SelectMany(x => x.Periodos.SelectMany(p => p.Empresas)).Concat(areas[inicial].Empresas).ToList();
            var fins = todosPer.Select(FimDoPeriodo).Where(d => d.HasValue).Select(d => d.Value).ToList();
            var inis = todosPer.Select(r => r.Periodo.Length >= 10 ? r.Periodo.Substring(0, 10) : "").Where(x => x != "").ToList();
            string periodo = fins.Count == 0 ? DateTime.Now.ToString("dd/MM/yyyy")
                : areas.Sum(x => x.Periodos.Count) > areas.Count
                    ? inis.OrderBy(x => DateTime.ParseExact(x, "dd/MM/yyyy", CultureInfo.InvariantCulture)).First() + " a " + fins.Max().ToString("dd/MM/yyyy")
                    : areas[inicial].Empresas.Select(r => r.Periodo).FirstOrDefault(p => p != "") ?? fins.Max().ToString("dd/MM/yyyy");
            string tituloArq = areas.Count == 1 ? areas[0].Titulo : "Painel de Vendas e Faturamento";
            string nome = tituloArq + " - " + periodo.Replace("/", "-") + ".html";
            foreach (char c in Path.GetInvalidFileNameChars()) nome = nome.Replace(c, '-');
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "Salvar o painel para enviar";
                dlg.Filter = "Página do painel (*.html)|*.html";
                dlg.FileName = nome;
                try { dlg.InitialDirectory = origem.ModoPasta ? Configuracao.Completo(origem.Pasta) : Environment.GetFolderPath(Environment.SpecialFolder.Desktop); } catch { }
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dlg.FileName, Exportador.GerarHtml(areas, inicial, SalaVendas.Carregar()), new UTF8Encoding(false));
                    long kb = Math.Max(1, new FileInfo(dlg.FileName).Length / 1024);
                    var resp = MessageBox.Show(this,
                        "Pronto! Arquivo salvo (" + kb + " KB):\n" + dlg.FileName + "\n\n" +
                        (areas.Count > 1 ? "Vão juntos no mesmo arquivo: " + string.Join(" e ", areas.Select(x => x.Titulo + " (" + Math.Max(1, x.Periodos.Count) + (x.Periodos.Count > 1 ? " períodos)" : " período)"))) + ".\nNo navegador troca a área no topo e o período em \"Ver o período\"."
                                         : "Vai só: " + areas[0].Titulo + (fora.Count > 0 ? "   (" + string.Join(", ", fora) + " ficou de fora: sem PDFs lidos)" : "")) +
                        "\n\nÉ só anexar no email. Quem receber abre no navegador (Chrome, Edge ou celular),\n" +
                        "sem instalar nada, com os mesmos filtros desta tela. O arquivo é uma foto dos dados de agora.\n\n" +
                        "Quer abrir a pasta do arquivo?", "Exportar", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (resp == DialogResult.Yes)
                        try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + dlg.FileName + "\""); } catch { }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Não consegui salvar o arquivo:\n" + ex.Message, "Exportar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        // Cada PDF é conferido com os totais da PRÓPRIA empresa
        void PreencherConferencia(List<Resumo> lista)
        {
            gradeConf.Rows.Clear();
            int problemas = 0, semPedidos = 0;
            foreach (var r in lista)
            {
                if (r.Vendedores.Count == 0) semPedidos++;
                foreach (var v in r.Vendedores.Concat(r.Zerados))
                {
                    if (v.Manual)
                    {
                        // vendedor criado no programa: só recebe as licitações
                        int im = gradeConf.Rows.Add(r.EmpresaCurta, v.Nome + "  (criado no programa)", v.Pedidos.Count, Tema.Reais(v.Total), "—", "", "recebeu licitações");
                        gradeConf.Rows[im].DefaultCellStyle.ForeColor = COR_LICITACAO;
                        continue;
                    }
                    // compara com o que veio no PDF, ANTES de passar as licitações
                    double lidoPdf = v.TotalLido;
                    int pedPdf = v.Pedidos.Count(p => !p.Licitacao || p.VendedorOriginal == null) + r.Vendedores.Concat(r.Zerados).SelectMany(x => x.Pedidos).Count(p => p.VendedorOriginal == v.Nome);
                    double? inf = v.TotalInformado;
                    double dif = inf.HasValue ? lidoPdf - inf.Value : 0;
                    string sit = !inf.HasValue ? "sem total no relatório" : Math.Abs(dif) < 0.005 ? "✓ confere" : "⚠ diferente";
                    if (inf.HasValue && Math.Abs(dif) >= 0.005) problemas++;
                    double recebido = v.Pedidos.Where(p => p.VendedorOriginal != null).Sum(p => p.Valor);
                    if (v.Transferido > 0.004) sit += "  ·  " + Tema.Reais(v.Transferido) + " foi p/ licitação";
                    if (recebido > 0.004) sit += "  ·  recebeu " + Tema.Reais(recebido) + " de licitação";
                    int i = gradeConf.Rows.Add(r.EmpresaCurta, v.Rotulo, pedPdf, Tema.Reais(lidoPdf), inf.HasValue ? Tema.Reais(inf.Value) : "—",
                                               inf.HasValue ? Tema.Reais(dif) : "—", sit);
                    gradeConf.Rows[i].Cells[6].Style.ForeColor = sit.StartsWith("✓") ? Tema.Bom : sit.StartsWith("⚠") ? Tema.Ruim : Tema.TextoSec;
                }
                foreach (var sv in r.SemVendedor)
                {
                    int i = gradeConf.Rows.Add(r.EmpresaCurta, "Sem vendedor" + (string.IsNullOrEmpty(sv.Codigo) ? "" : " (código " + sv.Codigo + ")"), sv.Pedidos.Count, Tema.Reais(sv.Total),
                                               sv.TotalInformado.HasValue ? Tema.Reais(sv.TotalInformado.Value) : "—", "", "desconsiderado no painel");
                    gradeConf.Rows[i].DefaultCellStyle.ForeColor = Tema.TextoSec;
                    gradeConf.Rows[i].DefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Italic);
                }
                // o TOTAL GERAL do PDF inclui as vendas sem vendedor
                double? g = r.TotalGeralInformado;
                double lidoTotal = r.TotalGeral + r.TotalSemVendedor;
                double dg = g.HasValue ? lidoTotal - g.Value : 0;
                string sg = !g.HasValue ? "TOTAL GERAL não encontrado" : Math.Abs(dg) < 0.005 ? "✓ confere" : "⚠ diferente";
                if (!g.HasValue || Math.Abs(dg) >= 0.005) problemas++;
                int rg = gradeConf.Rows.Add(r.EmpresaCurta, "TOTAL GERAL" + (r.SemVendedor.Count > 0 ? " (com os sem vendedor)" : ""), r.Pedidos.Count() + r.PedidosSemVendedor, Tema.Reais(lidoTotal), g.HasValue ? Tema.Reais(g.Value) : "—", g.HasValue ? Tema.Reais(dg) : "—", sg);
                gradeConf.Rows[rg].DefaultCellStyle.Font = Tema.Fonte(9.75f, true);
                gradeConf.Rows[rg].DefaultCellStyle.BackColor = Tema.Cor("#F7F7F5");
                gradeConf.Rows[rg].Cells[6].Style.ForeColor = sg.StartsWith("✓") ? Tema.Bom : Tema.Ruim;
            }
            if (lista.Count > 1)
            {
                int rs = gradeConf.Rows.Add("SOMA", "TOTAL DAS " + lista.Count + " EMPRESAS (painel, sem os sem vendedor)", lista.Sum(r => r.Pedidos.Count()), Tema.Reais(lista.Sum(r => r.TotalGeral)),
                                            lista.All(r => r.TotalGeralInformado.HasValue) ? Tema.Reais(lista.Sum(r => r.TotalGeralInformado.Value)) : "—", "", "");
                gradeConf.Rows[rs].DefaultCellStyle.Font = Tema.Fonte(10f, true);
                gradeConf.Rows[rs].DefaultCellStyle.BackColor = Tema.DestaqueClaro;
            }
            gradeConf.ClearSelection();

            int nr = lista.Sum(r => r.NaoReconhecidas.Count);
            if (semPedidos == lista.Count)
            {
                lblConf.Text = (ehFaturamento ? "⚠  Nenhuma nota foi encontrada." : "⚠  Nenhum pedido foi encontrado.") + " Confira o texto lido abaixo.";
                lblConf.BackColor = Tema.Cor("#FBE6E6"); lblConf.ForeColor = Tema.Ruim;
            }
            else if (problemas == 0)
            {
                double fora = lista.Sum(r => r.TotalSemVendedor);
                lblConf.Text = "✓  Tudo confere: em " + (lista.Count > 1 ? "todas as " + lista.Count + " empresas" : "todo o relatório") +
                               ", a soma " + (ehFaturamento ? "das notas lidas" : "dos pedidos lidos") + " bate com os totais impressos." +
                               (fora > 0.004 ? "   Sem vendedor (fora do painel): " + Tema.Reais(fora) + "." : "") +
                               (nr > 0 ? "   (" + nr + " linha(s) não reconhecida(s), ver abaixo)" : "");
                lblConf.BackColor = Tema.Cor("#E3F1E6"); lblConf.ForeColor = Tema.Bom;
            }
            else
            {
                lblConf.Text = "⚠  " + problemas + " total(is) não bate(m) com o relatório. Veja a coluna Situação e o texto lido abaixo.";
                lblConf.BackColor = Tema.Cor("#FBE6E6"); lblConf.ForeColor = Tema.Ruim;
            }

            AtualizarSelo(problemas, semPedidos == lista.Count);
            var linhas = new List<string>();
            foreach (var r in lista)
            {
                linhas.Add("########## " + r.EmpresaCurta + "   (" + Path.GetFileName(r.Arquivo) + ")   período " + r.Periodo + " ##########");
                if (r.NaoReconhecidas.Count > 0)
                {
                    linhas.Add("----- LINHAS NÃO RECONHECIDAS (" + r.NaoReconhecidas.Count + ") -----");
                    linhas.AddRange(r.NaoReconhecidas);
                    linhas.Add("----- TEXTO LIDO -----");
                }
                linhas.AddRange(r.TextoLido);
                linhas.Add("");
            }
            txtLido.Text = string.Join("\r\n", linhas);
        }
    }
}
