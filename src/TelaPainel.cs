// =====================================================================
//  TelaPainel.cs  -  A tela principal, em FLUXO (de cima para baixo):
//
//   Vendedores: todos ▾ · buscar cliente · só licitação   (frase só com filtro)
//   ┌ TOTAL (número grande) ▲▼ ───────────────────────┐ ┌ Licitação ┐
//   │ [■■■ ALFA ■■■■■■ BETA ■■■■■■■ GAMA]   │ │           │
//   └──────────────────────────────────────────────────┘ └───────────┘
//   ┌ Vendedores | Clientes            caminho: Todos › FULANO › CLIENTE ┐
//   │ 1 FULANO  ███████████████  R$ ...  ▲            (clicou: clientes  │
//   │   └ clientes dele aqui embaixo → clicou no cliente: os pedidos)   │
//   └───────────────────────────────────────────────────────────────────┘
//  Tudo é calculado a partir de uma lista "achatada" de pedidos.
// =====================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PainelPedidos
{
    // Estado dos filtros + contas (fica separado da parte visual)
    class Filtros
    {
        public string Empresa;                                   // null = todas
        public HashSet<string> Vendedores = new HashSet<string>(); // chaves normalizadas; vazio = todos
        public string Busca = "";
        public bool SoLicitacao;

        public static string ChaveV(Pedido p) { return Resumo.Normalizar(p.Vendedor.Nome); }
        public static string ChaveC(Pedido p) { return Resumo.Normalizar(p.Cliente); }

        public IEnumerable<Pedido> Aplicar(IEnumerable<Pedido> peds, bool considerarVendedor)
        {
            string q = Resumo.Normalizar(Busca);
            foreach (var p in peds)
            {
                if (Empresa != null && p.Empresa != Empresa) continue;
                if (considerarVendedor && Vendedores.Count > 0 && !Vendedores.Contains(ChaveV(p))) continue;
                if (q != "" && !ChaveC(p).Contains(q)) continue;
                if (SoLicitacao && !p.Licitacao) continue;
                yield return p;
            }
        }

        public bool Algum { get { return Empresa != null || Vendedores.Count > 0 || Busca.Trim() != "" || SoLicitacao; } }
    }

    // Caixa branca com cantos arredondados (fundo das seções)
    class Caixa : Panel
    {
        public Caixa() { DoubleBuffered = true; BackColor = Tema.Cartao; Padding = new Padding(16, 12, 16, 12); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Tema.Fundo);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Desenho.Arredondado(r, 10)) using (var f = new SolidBrush(Tema.Cartao)) using (var b = new Pen(Tema.Borda))
            { e.Graphics.FillPath(f, path); e.Graphics.DrawPath(b, path); }
        }
    }

    static class Desenho
    {
        public static GraphicsPath Arredondado(RectangleF r, float raio)
        {
            var p = new GraphicsPath();
            float d = Math.Min(raio * 2, Math.Min(r.Width, r.Height));
            if (d < 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // "▲ 12%" / "▼ 8%" / "novo"; null quando não há base de comparação
        public static string Variacao(double atual, double anterior, out Color cor)
        {
            cor = Tema.TextoSec;
            if (anterior < 0.005 && atual < 0.005) return null;
            if (anterior < 0.005) { cor = Tema.Bom; return "novo"; }
            double v = (atual / anterior - 1) * 100;
            if (Math.Abs(v) < 0.5) return "= " + "0%";
            cor = v > 0 ? Tema.Bom : Tema.Ruim;
            return (v > 0 ? "▲ " : "▼ ") + Math.Abs(v).ToString(Math.Abs(v) < 10 ? "0.0" : "0", Tema.BR) + "%";
        }
    }

    class TelaPainel : UserControl
    {
        // "pedido" (Vendas) ou "nota" (Faturamento): só muda as palavras da tela
        public bool Notas;
        public string Itens { get { return Notas ? "notas" : "pedidos"; } }
        string Item1 { get { return Notas ? "Nota" : "Pedido"; } }
        public string ItensCurto { get { return Notas ? "NF" : "ped."; } }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        // ---------------- dados ----------------
        List<Resumo> atuais = new List<Resumo>(), anteriores = new List<Resumo>();
        List<Pedido> todos = new List<Pedido>(), todosAnt = new List<Pedido>();
        public List<string> Empresas = new List<string>();
        public readonly Filtros F = new Filtros();

        // ---------------- navegação (o "fluxo") ----------------
        //   aba 0 = Vendedores (clicar abre os clientes do vendedor ali embaixo)
        //   aba 1 = Clientes   (clicar abre os pedidos do cliente)
        public int Aba;
        public string VendAberto;        // vendedor com os clientes abertos (aba 0)
        public string ClienteAberto;     // cliente com os pedidos abertos (aba 1)
        public string VendOrigem;        // de qual vendedor veio o cliente aberto (para o caminho voltar)
        public bool ModoPorEmpresa;
        bool montando;

        public event Action<string, string> PedirLicitacao;   // (cliente, vendedor)
        public event Action PedirListaLicitacao;

        // ---------------- controles ----------------
        readonly FlowLayoutPanel linhaFiltros = new FlowLayoutPanel();
        readonly Button btnVend = new Button();
        readonly TextBox txtBusca = new TextBox();
        readonly CheckBox chkLic = new CheckBox();
        readonly LinkLabel lnkLimpar = new LinkLabel();
        readonly Label lblFrase = new Label();
        readonly Hero hero;
        readonly CabCorpo cab;
        readonly Panel rolagem = new Panel();
        readonly ListaVend lista;
        readonly DataGridView grade = new DataGridView();
        readonly Button btnLic = new Button();
        readonly ToolStripDropDown menuVend = new ToolStripDropDown();
        readonly CheckedListBox listaVend = new CheckedListBox();
        public readonly ToolTip Dica = new ToolTip { InitialDelay = 250, ReshowDelay = 100, AutoPopDelay = 15000 };

        public TelaPainel()
        {
            BackColor = Tema.Fundo;
            Padding = new Padding(24, 12, 24, 18);
            Font = Tema.Fonte(9.75f);
            hero = new Hero(this);
            cab = new CabCorpo(this);
            lista = new ListaVend(this);

            // ---------- linha de filtros ("pílulas") ----------
            linhaFiltros.Dock = DockStyle.Top; linhaFiltros.Height = 40; linhaFiltros.WrapContents = false; linhaFiltros.BackColor = Tema.Fundo;
            btnVend.FlatStyle = FlatStyle.Flat; btnVend.FlatAppearance.BorderColor = Tema.Borda; btnVend.BackColor = Tema.Cartao;
            btnVend.AutoSize = true; btnVend.MinimumSize = new Size(170, 30); btnVend.Height = 30; btnVend.Margin = new Padding(0, 3, 10, 0); btnVend.Cursor = Cursors.Hand;
            btnVend.Padding = new Padding(6, 0, 6, 0);
            btnVend.Click += delegate { AbrirMenuVendedores(); };
            txtBusca.Width = 240; txtBusca.Margin = new Padding(0, 7, 10, 0); txtBusca.Font = Tema.Fonte(10f);
            txtBusca.TextChanged += delegate { if (montando) return; F.Busca = txtBusca.Text; ClienteAberto = null; Atualizar(); };
            chkLic.Text = "Só licitação"; chkLic.AutoSize = true; chkLic.ForeColor = Tema.Cor("#6250D6"); chkLic.Margin = new Padding(4, 9, 14, 0);
            chkLic.CheckedChanged += delegate { if (montando) return; F.SoLicitacao = chkLic.Checked; Atualizar(); };
            lnkLimpar.Text = "Limpar filtros"; lnkLimpar.AutoSize = true; lnkLimpar.Margin = new Padding(0, 10, 16, 0); lnkLimpar.LinkColor = Tema.Destaque;
            lnkLimpar.LinkClicked += delegate { Limpar(); };
            lblFrase.AutoSize = true; lblFrase.ForeColor = Tema.TextoSec; lblFrase.Margin = new Padding(0, 10, 0, 0); lblFrase.UseMnemonic = false;
            linhaFiltros.Controls.AddRange(new Control[] { btnVend, txtBusca, chkLic, lnkLimpar, lblFrase });

            listaVend.CheckOnClick = true; listaVend.BorderStyle = BorderStyle.None; listaVend.Font = Tema.Fonte(10f);
            listaVend.ItemCheck += (s, e) =>
            {
                // ignora quando é o próprio programa montando a lista (senão vira um laço sem fim)
                if (montando) return;
                BeginInvoke(new Action(() =>
                {
                    F.Vendedores = new HashSet<string>(listaVend.CheckedItems.Cast<object>().Select(o => Resumo.Normalizar(o.ToString())));
                    ClienteAberto = null;
                    Atualizar();
                }));
            };
            var host = new ToolStripControlHost(listaVend) { AutoSize = false, Size = new Size(260, 300), Margin = Padding.Empty, Padding = Padding.Empty };
            menuVend.Items.Add(host); menuVend.Padding = new Padding(4);

            // ---------- número grande + empresas + licitação ----------
            hero.Dock = DockStyle.Top; hero.Height = 168;

            // ---------- corpo: Vendedores | Clientes ----------
            var corpo = new Caixa { Dock = DockStyle.Fill, Padding = new Padding(18, 6, 18, 10) };
            cab.Dock = DockStyle.Top; cab.Height = 48;
            rolagem.Dock = DockStyle.Fill; rolagem.AutoScroll = true; rolagem.BackColor = Tema.Cartao;
            lista.Location = new Point(0, 0);
            rolagem.Controls.Add(lista);
            rolagem.Resize += delegate { AjustarLista(); };

            Tema.EstilizarGrade(grade); grade.Dock = DockStyle.Fill; grade.BorderStyle = BorderStyle.None;
            grade.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grade.CellClick += (s, e) =>
            {
                if (e.RowIndex < 0 || ClienteAberto != null) return;
                var chave = grade.Rows[e.RowIndex].Tag as string;
                if (chave != null) { ClienteAberto = chave; VendOrigem = null; Atualizar(); }
            };
            // botão direito num cliente: marcar/desmarcar licitação direto
            var menuCli = new ContextMenuStrip();
            string chaveMenu = null;
            var itemLic = new ToolStripMenuItem("Marcar como licitação");
            itemLic.Click += delegate { PedirLicitacaoDe(chaveMenu); };
            var itemVer = new ToolStripMenuItem("Ver os " + Itens + " deste cliente");
            itemVer.Click += delegate { if (chaveMenu != null) { ClienteAberto = chaveMenu; VendOrigem = null; Atualizar(); } };
            menuCli.Items.AddRange(new ToolStripItem[] { itemLic, itemVer });
            grade.CellMouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
                string chave = ClienteAberto ?? grade.Rows[e.RowIndex].Tag as string;
                if (chave == null) return;
                chaveMenu = chave;
                var nome = todos.Where(x => Filtros.ChaveC(x) == chave).Select(x => x.Cliente).FirstOrDefault() ?? "";
                itemLic.Text = Licitacoes.EhLicitacao(Licitacoes.Carregar(), nome) ? "Desmarcar licitação" : "Marcar como licitação";
                itemVer.Text = "Ver os " + Itens + " deste cliente";
                itemVer.Visible = ClienteAberto == null;
                grade.ClearSelection(); grade.Rows[e.RowIndex].Selected = true;
                menuCli.Show(grade, grade.PointToClient(Cursor.Position));
            };
            grade.CellMouseEnter += (s, e) => { grade.Cursor = ClienteAberto == null && e.RowIndex >= 0 ? Cursors.Hand : Cursors.Default; };

            btnLic.AutoSize = true; btnLic.FlatStyle = FlatStyle.Flat; btnLic.Cursor = Cursors.Hand; btnLic.Visible = false;
            btnLic.Font = Tema.Fonte(9.5f, true); btnLic.Padding = new Padding(6, 0, 6, 0); btnLic.UseMnemonic = false;
            btnLic.FlatAppearance.BorderColor = Tema.Cor("#6250D6");
            btnLic.Click += delegate { PedirLicitacaoDe(ClienteAberto); };
            cab.Controls.Add(btnLic);
            cab.Resize += delegate { btnLic.Location = new Point(cab.Width - btnLic.Width - 2, 8); };

            corpo.Controls.Add(rolagem); corpo.Controls.Add(grade); corpo.Controls.Add(cab);

            Controls.Add(corpo);
            Controls.Add(new Panel { Dock = DockStyle.Top, Height = 14, BackColor = Tema.Fundo });
            Controls.Add(hero);
            Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Tema.Fundo });
            Controls.Add(linhaFiltros);

            HandleCreated += delegate
            {
                try { SendMessage(txtBusca.Handle, 0x1501, (IntPtr)1, "🔍 buscar cliente (razão social)"); } catch { }
            };
        }

        public void PedirListaLic() { if (PedirListaLicitacao != null) PedirListaLicitacao(); }

        void PedirLicitacaoDe(string chaveCliente)
        {
            if (chaveCliente == null || PedirLicitacao == null) return;
            var peds = todos.Where(x => Filtros.ChaveC(x) == chaveCliente).ToList();
            if (peds.Count == 0) return;
            string nome = peds.GroupBy(p => p.Cliente).OrderByDescending(x => x.Count()).First().Key;
            var vends = peds.Select(p => p.Vendedor.Nome).Distinct().ToList();
            PedirLicitacao(nome, string.Join(", ", vends));
        }

        // ================== dados ==================
        public void Definir(List<Resumo> atuais, List<Resumo> anteriores)
        {
            this.atuais = atuais; this.anteriores = anteriores ?? new List<Resumo>();
            todos = atuais.SelectMany(r => r.Vendedores).SelectMany(v => v.Pedidos).ToList();
            todosAnt = this.anteriores.SelectMany(r => r.Vendedores).SelectMany(v => v.Pedidos).ToList();
            CortarAnterior();
            TotalSemRestricao = todos.Sum(p => p.Valor);
            if (Restrito != null)   // SALA DE VENDAS: só os vendedores escolhidos
            {
                todos = todos.Where(p => Restrito.Contains(Filtros.ChaveV(p))).ToList();
                todosAnt = todosAnt.Where(p => Restrito.Contains(Filtros.ChaveV(p))).ToList();
                F.Vendedores.RemoveWhere(k => !Restrito.Contains(k));
            }
            Empresas = atuais.Select(r => r.EmpresaCurta).ToList();
            if (F.Empresa != null && !Empresas.Contains(F.Empresa)) F.Empresa = null;
            if (VendAberto != null && !todos.Any(p => Filtros.ChaveV(p) == VendAberto)) VendAberto = null;
            Atualizar();
        }

        // ---- ▲▼ justo com o período em andamento ----
        // Se o período atual ainda não terminou (ex.: 01/11 a 30/11, relatório tirado em 16/11),
        // compara com os MESMOS DIAS do período anterior (01/10 a 16/10), e não com ele inteiro.
        // A data de referência é a do relatório (Data: dd/mm/aaaa do PDF); sem ela, hoje.
        public DateTime? Hoje;   // só para teste
        public string ComparacaoTexto { get; private set; }

        static DateTime? Data(string s, int pos)
        {
            var m = System.Text.RegularExpressions.Regex.Matches(s ?? "", @"\d{2}/\d{2}/\d{4}");
            if (m.Count <= pos) return null;
            DateTime d;
            return DateTime.TryParseExact(m[pos].Value, "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d) ? d : (DateTime?)null;
        }

        void CortarAnterior()
        {
            var cortes = new Dictionary<string, DateTime>();
            var textos = new List<string>();
            foreach (var ant in anteriores)
            {
                var at = atuais.FirstOrDefault(r => r.EmpresaCurta == ant.EmpresaCurta);
                DateTime? iniAt = at == null ? null : Data(at.Periodo, 0), fimAt = at == null ? null : Data(at.Periodo, 1);
                DateTime? iniAn = Data(ant.Periodo, 0), fimAn = Data(ant.Periodo, 1);
                string txt = ant.Periodo;
                if (iniAt.HasValue && fimAt.HasValue && iniAn.HasValue && fimAn.HasValue)
                {
                    DateTime refe = (Hoje ?? Data(at.Emissao, 0) ?? DateTime.Today).Date;
                    if (refe >= iniAt.Value && refe < fimAt.Value)
                    {
                        var corte = iniAn.Value.AddDays((refe - iniAt.Value).TotalDays);
                        if (corte < fimAn.Value)
                        {
                            cortes[ant.EmpresaCurta] = corte;
                            txt = iniAn.Value.ToString("dd/MM") + " a " + corte.ToString("dd/MM/yyyy") + " (mesmos dias)";
                        }
                    }
                }
                if (txt != "" && !textos.Contains(txt)) textos.Add(txt);
            }
            if (cortes.Count > 0)
                todosAnt = todosAnt.Where(p => { DateTime c; return !cortes.TryGetValue(p.Empresa, out c) || !p.Data.HasValue || p.Data.Value.Date <= c; }).ToList();
            ComparacaoTexto = string.Join(" / ", textos);
        }

        // Sala de Vendas: quando não é null, a tela inteira mostra só esses vendedores (chaves normalizadas)
        public HashSet<string> Restrito;
        public string RotuloTotal;              // ex.: "Total da sala de vendas"
        public double TotalSemRestricao { get; private set; }

        public bool TemAnterior { get { return anteriores.Count > 0; } }
        public string PeriodoAnterior { get { return string.Join(" / ", anteriores.Select(r => r.Periodo).Where(p => p != "").Distinct()); } }
        public List<Pedido> Todos { get { return todos; } }
        public List<Pedido> TodosAnteriores { get { return todosAnt; } }
        // ▲▼ só compara o que dá para comparar: empresas que TÊM período anterior.
        public bool TemAnteriorDe(string empresa) { return anteriores.Any(r => r.EmpresaCurta == empresa); }
        public double Comparavel(IEnumerable<Pedido> ps) { return ps.Where(p => TemAnteriorDe(p.Empresa)).Sum(p => p.Valor); }

        void Limpar()
        {
            F.Empresa = null; F.Vendedores.Clear(); F.Busca = ""; F.SoLicitacao = false; ClienteAberto = null;
            montando = true; txtBusca.Text = ""; chkLic.Checked = false; montando = false;
            Atualizar();
        }

        // ---------------- ações (cliques) ----------------
        public void ClicarEmpresa(string nome) { F.Empresa = F.Empresa == nome ? null : nome; ClienteAberto = null; Atualizar(); }
        public void AlternarLicitacao() { F.SoLicitacao = !F.SoLicitacao; ClienteAberto = null; Atualizar(); }
        public void IrAba(int aba) { Aba = aba; ClienteAberto = null; VendOrigem = null; Atualizar(); }
        public void AbrirVendedor(string chave) { VendAberto = VendAberto == chave ? null : chave; ModoPorEmpresa = false; Atualizar(); }
        public void AbrirClienteDoVendedor(string cliente, string vendedor) { Aba = 1; ClienteAberto = cliente; VendOrigem = vendedor; Atualizar(); }
        public void VerTodosClientesDe(string vendedor) { F.Vendedores = new HashSet<string> { vendedor }; Aba = 1; ClienteAberto = null; VendOrigem = null; Atualizar(); }
        public void TrocarModo(bool porEmpresa) { ModoPorEmpresa = porEmpresa; Atualizar(); }
        public void Voltar(int nivel)
        {
            // caminho: 0 = raiz da aba; 1 = vendedor (quando o cliente veio de um vendedor)
            if (Aba == 0) { VendAberto = null; }
            else if (VendOrigem != null) { Aba = 0; VendAberto = nivel == 0 ? null : VendOrigem; ClienteAberto = null; VendOrigem = null; }
            else ClienteAberto = null;
            Atualizar();
        }

        public string NomeVend(string chave)
        {
            var p = todos.FirstOrDefault(x => Filtros.ChaveV(x) == chave);
            return p != null ? p.Vendedor.Nome : chave;
        }
        public string NomeCliente(string chave)
        {
            var ps = todos.Where(x => Filtros.ChaveC(x) == chave).ToList();
            return ps.Count == 0 ? chave : ps.GroupBy(p => p.Cliente).OrderByDescending(x => x.Count()).First().Key;
        }

        // ================== redesenho ==================
        public void Atualizar()
        {
            // vendedores (pílula + lista)
            var nomesVend = Filtrar(todos, false).GroupBy(Filtros.ChaveV).OrderByDescending(g => g.Sum(p => p.Valor)).Select(g => g.First().Vendedor.Nome).ToList();
            foreach (var k in F.Vendedores) if (!nomesVend.Any(n => Resumo.Normalizar(n) == k)) { var p = todos.FirstOrDefault(x => Filtros.ChaveV(x) == k); if (p != null) nomesVend.Add(p.Vendedor.Nome); }
            montando = true;
            listaVend.Items.Clear();
            foreach (var n in nomesVend) listaVend.Items.Add(n, F.Vendedores.Contains(Resumo.Normalizar(n)));
            if (chkLic.Checked != F.SoLicitacao) chkLic.Checked = F.SoLicitacao;
            if (txtBusca.Text != F.Busca) txtBusca.Text = F.Busca;
            montando = false;
            btnVend.Text = "Vendedores:  " + (F.Vendedores.Count == 0 ? (Restrito != null ? "todos da sala" : "todos") : NomesEscolhidos(2)) + "   ▾";
            btnVend.Font = Tema.Fonte(9.75f, F.Vendedores.Count > 0);
            btnVend.ForeColor = F.Vendedores.Count > 0 ? Tema.Destaque : Tema.Texto;
            btnVend.FlatAppearance.BorderColor = F.Vendedores.Count > 0 ? Tema.Destaque : Tema.Borda;
            lnkLimpar.Visible = F.Algum;

            // frase: SÓ quando há filtro (sem filtro ela repetiria o número grande)
            var filtrados = Filtrar(todos, true).ToList();
            double totalTudo = todos.Sum(p => p.Valor), totalF = filtrados.Sum(p => p.Valor);
            if (F.Algum)
            {
                var partes = new List<string>();
                if (F.Vendedores.Count > 0) partes.Add(NomesEscolhidos(3));
                if (F.Empresa != null) partes.Add(F.Empresa);
                if (F.Busca.Trim() != "") partes.Add("clientes com “" + F.Busca.Trim() + "”");
                if (F.SoLicitacao) partes.Add("só licitação");
                lblFrase.Text = "Mostrando " + string.Join(" · ", partes) + "  →  " + filtrados.Count + " " + Itens + " · " + Tema.Reais(totalF) +
                                (totalTudo > 0 ? "  (" + (totalF / totalTudo * 100).ToString("0.0", Tema.BR) + "% do total)" : "");
                lblFrase.Visible = true;
            }
            else lblFrase.Visible = false;

            hero.Invalidate();
            if (ClienteAberto != null && !filtrados.Any(p => Filtros.ChaveC(p) == ClienteAberto)) { ClienteAberto = null; VendOrigem = null; }
            if (Aba == 0)
            {
                grade.Visible = false; rolagem.Visible = true; btnLic.Visible = false;
                AjustarLista();
            }
            else
            {
                rolagem.Visible = false; grade.Visible = true;
                PreencherGrade(filtrados);
            }
            cab.Invalidate();
        }

        void AjustarLista()
        {
            int w = rolagem.ClientSize.Width;
            int h = lista.AlturaNecessaria(w);
            if (h > rolagem.ClientSize.Height) w = rolagem.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
            lista.Size = new Size(Math.Max(100, w), Math.Max(lista.AlturaNecessaria(w), rolagem.ClientSize.Height));
            lista.Invalidate();
        }

        string NomesEscolhidos(int max)
        {
            var nomes = F.Vendedores.Select(NomeVend).OrderBy(x => x).ToList();
            if (nomes.Count <= max) return string.Join(" e ", nomes);
            return string.Join(", ", nomes.Take(max)) + " +" + (nomes.Count - max);
        }

        public IEnumerable<Pedido> Filtrar(IEnumerable<Pedido> peds, bool considerarVendedor) { return F.Aplicar(peds, considerarVendedor); }

        void AbrirMenuVendedores()
        {
            listaVend.Height = Math.Min(360, Math.Max(80, listaVend.Items.Count * listaVend.ItemHeight + 8));
            ((ToolStripControlHost)menuVend.Items[0]).Size = new Size(Math.Max(btnVend.Width, 260), listaVend.Height);
            menuVend.Show(btnVend, new Point(0, btnVend.Height));
        }

        // ================== aba Clientes (lista e pedidos) ==================
        public string InfoClientes = "";

        void PreencherGrade(List<Pedido> filtrados)
        {
            grade.Rows.Clear(); grade.Columns.Clear();
            var regras = Licitacoes.Carregar();
            if (ClienteAberto == null)
            {
                btnLic.Visible = false;
                var grupos = filtrados.GroupBy(Filtros.ChaveC).Select(g => new
                {
                    Chave = g.Key,
                    Nome = g.GroupBy(p => p.Cliente).OrderByDescending(x => x.Count()).First().Key,
                    Vend = g.GroupBy(Filtros.ChaveV).OrderByDescending(x => x.Sum(p => p.Valor)).Select(x => PrimeiroNome(x.First().Vendedor.Nome)).ToList(),
                    Ped = g.Count(), Valor = g.Sum(p => p.Valor), Lic = g.Any(p => p.Licitacao), Comp = Comparavel(g)
                }).OrderByDescending(x => x.Valor).ToList();
                double total = grupos.Sum(x => x.Valor);
                InfoClientes = NCli(grupos.Count) + " · " + Tema.Reais(total) + "  —  clique para ver os " + Itens + " · botão direito: licitação";

                Tema.Coluna(grade, "Cliente", 170); Tema.Coluna(grade, "Vendedor(es)", 70);
                Tema.Coluna(grade, "Valor", 50, true); Tema.Coluna(grade, "", 26, true); Tema.Coluna(grade, "%", 24, true);
                var ant = Filtrar(todosAnt, true).GroupBy(Filtros.ChaveC).ToDictionary(g => g.Key, g => g.Sum(p => p.Valor));
                foreach (var c in grupos)
                {
                    // ▲▼ só para quem também comprou no período anterior (sem "novo" em todo mundo)
                    double a; ant.TryGetValue(c.Chave, out a);
                    Color corVar = Tema.TextoSec;
                    string v = a > 0.005 ? Desenho.Variacao(c.Comp, a, out corVar) : null;
                    string nome = c.Nome + (c.Ped > 1 ? "     " + c.Ped + " " + ItensCurto : "") + (c.Lic ? "     (licitação)" : "");
                    int r = grade.Rows.Add(nome, string.Join(", ", c.Vend.Take(3)) + (c.Vend.Count > 3 ? " +" + (c.Vend.Count - 3) : ""),
                                           Tema.Reais(c.Valor), v ?? "", total > 0 ? (c.Valor / total * 100).ToString("0.0", Tema.BR) + "%" : "");
                    grade.Rows[r].Tag = c.Chave;
                    if (c.Lic) grade.Rows[r].DefaultCellStyle.ForeColor = Tema.Cor("#6250D6");
                    grade.Rows[r].Cells[3].Style.ForeColor = corVar;
                    grade.Rows[r].Cells[3].Style.Font = Tema.Fonte(8.75f, true);
                }
            }
            else
            {
                // veio de um vendedor ("ÁLVARO › CLIENTE")? mostra só as vendas dele para esse cliente
                var peds = filtrados.Where(p => Filtros.ChaveC(p) == ClienteAberto && (VendOrigem == null || Filtros.ChaveV(p) == VendOrigem)).OrderBy(p => p.Data).ToList();
                if (peds.Count == 0) { VendOrigem = null; peds = filtrados.Where(p => Filtros.ChaveC(p) == ClienteAberto).OrderBy(p => p.Data).ToList(); }
                string nome = peds.GroupBy(p => p.Cliente).OrderByDescending(x => x.Count()).First().Key;
                bool lic = Licitacoes.EhLicitacao(regras, nome);
                InfoClientes = peds.Count + " " + Itens + " · " + Tema.Reais(peds.Sum(p => p.Valor)) + " · " + string.Join(", ", peds.Select(p => p.Empresa).Distinct());
                btnLic.Visible = true;
                btnLic.Text = lic ? "✓ Licitação  —  desmarcar" : "Marcar como licitação";
                btnLic.BackColor = lic ? Tema.Cartao : Tema.Cor("#6250D6");
                btnLic.ForeColor = lic ? Tema.Cor("#6250D6") : Color.White;
                btnLic.Location = new Point(cab.Width - btnLic.Width - 2, 8);

                Tema.Coluna(grade, "Empresa", 40); Tema.Coluna(grade, Item1, 34); Tema.Coluna(grade, "Data", 40);
                Tema.Coluna(grade, "Vendedor", 80); if (!Notas) Tema.Coluna(grade, "Quantidade", 40, true); Tema.Coluna(grade, "Valor", 50, true);
                foreach (var p in peds)
                {
                    string dt = p.Data.HasValue ? p.Data.Value.ToString("dd/MM/yyyy") : "";
                    int r = Notas ? grade.Rows.Add(p.Empresa, p.Numero, dt, p.Vendedor.Nome, Tema.Reais(p.Valor))
                                  : grade.Rows.Add(p.Empresa, p.Numero, dt, p.Vendedor.Nome, Tema.Qtd(p.Quantidade), Tema.Reais(p.Valor));
                    if (p.Licitacao) grade.Rows[r].DefaultCellStyle.ForeColor = Tema.Cor("#6250D6");
                }
            }
            grade.ClearSelection();
        }

        public static string NCli(int n) { return n + (n == 1 ? " cliente" : " clientes"); }

        public static string PrimeiroNome(string nome)
        {
            var p = nome.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return p.Length == 0 ? nome : p[0];
        }
    }

    // ------------------------------------------------------------------
    // Topo da tela: UM número grande (total) com ▲▼, as empresas numa
    // barra só (clicar filtra) e, ao lado, o cartão de Licitação.
    // ------------------------------------------------------------------
    class Hero : Control
    {
        readonly TelaPainel tela;
        readonly List<KeyValuePair<RectangleF, string>> alvos = new List<KeyValuePair<RectangleF, string>>();
        RectangleF alvoLic;

        public Hero(TelaPainel t)
        {
            tela = t;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Tema.Fundo;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = alvos.Any(a => a.Key.Contains(e.Location)) || alvoLic.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (alvoLic.Contains(e.Location)) { tela.AlternarLicitacao(); return; }
            foreach (var a in alvos) if (a.Key.Contains(e.Location)) { tela.ClicarEmpresa(a.Value); return; }
        }

        static void Cartao(Graphics g, RectangleF r)
        {
            using (var path = Desenho.Arredondado(r, 12)) using (var f = new SolidBrush(Tema.Cartao)) using (var b = new Pen(Tema.Borda))
            { g.FillPath(f, path); g.DrawPath(b, path); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Tema.Fundo);
            alvos.Clear(); alvoLic = RectangleF.Empty;
            var F = tela.F;
            // contas SEM o filtro de empresa (cada empresa tem o seu valor), com os outros filtros
            var semEmp = new Filtros { Vendedores = F.Vendedores, Busca = F.Busca, SoLicitacao = F.SoLicitacao };
            var base_ = semEmp.Aplicar(tela.Todos, true).ToList();
            var baseAnt = semEmp.Aplicar(tela.TodosAnteriores, true).ToList();
            var filt = F.Empresa == null ? base_ : base_.Where(p => p.Empresa == F.Empresa).ToList();
            var filtAnt = F.Empresa == null ? baseAnt : baseAnt.Where(p => p.Empresa == F.Empresa).ToList();
            bool comparar = tela.TemAnterior;
            bool temLic = tela.Todos.Any(p => p.Licitacao);

            float wLic = temLic ? 330 : 0, gap = temLic ? 16 : 0;
            var rb = new RectangleF(0.5f, 0.5f, Width - wLic - gap - 1.5f, Height - 1.5f);
            Cartao(g, rb);
            var fmtNo = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            float x = rb.X + 24;

            string rot = F.Algum ? "Total do filtro" : tela.RotuloTotal ?? (tela.Empresas.Count > 1 ? "Total vendido · " + tela.Empresas.Count + " empresas" : "Total vendido");
            if (tela.Notas && !F.Algum && tela.RotuloTotal == null) rot = rot.Replace("vendido", "faturado");
            using (var f = Tema.Fonte(9.75f)) using (var b = new SolidBrush(Tema.TextoSec)) g.DrawString(rot, f, b, x, 14);
            double tot = filt.Sum(p => p.Valor);
            string valor = Tema.Reais(tot);
            float wv;
            using (var f = Tema.Fonte(25f, true))
            {
                wv = g.MeasureString(valor, f).Width;
                using (var b = new SolidBrush(Tema.Texto)) g.DrawString(valor, f, b, x - 3, 32);
            }
            if (comparar)
            {
                Color cv; string v = Desenho.Variacao(tela.Comparavel(filt), filtAnt.Sum(p => p.Valor), out cv);
                if (v != null)
                {
                    using (var f = Tema.Fonte(10.5f, true)) using (var b = new SolidBrush(cv)) g.DrawString(v, f, b, x + wv + 4, 48);
                    float wvar; using (var f = Tema.Fonte(10.5f, true)) wvar = g.MeasureString(v, f).Width;
                    using (var f = Tema.Fonte(9.25f)) using (var b = new SolidBrush(Tema.TextoSec))
                        g.DrawString("vs " + tela.ComparacaoTexto, f, b, new RectangleF(x + wv + 6 + wvar, 50, Math.Max(10, rb.Right - (x + wv + 6 + wvar) - 16), 20), fmtNo);
                }
            }
            using (var f = Tema.Fonte(9.25f)) using (var b = new SolidBrush(Tema.TextoSec))
                g.DrawString(filt.Count + " " + tela.Itens + " · " + TelaPainel.NCli(filt.Select(Filtros.ChaveC).Distinct().Count()), f, b, x, 78);

            // barra única com as empresas + valores embaixo
            var emps = tela.Empresas;
            if (emps.Count > 1)
            {
                double somaBase = base_.Sum(p => p.Valor);
                float by = 104, bh = 10, bw = rb.Right - 24 - x, bx = x;
                using (var fundo = new SolidBrush(Tema.Grade)) using (var path = Desenho.Arredondado(new RectangleF(x, by, bw, bh), 5)) g.FillPath(fundo, path);
                float colW = Math.Min(260, bw / emps.Count);
                for (int i = 0; i < emps.Count; i++)
                {
                    string emp = emps[i];
                    var pe = base_.Where(p => p.Empresa == emp).ToList();
                    double soma = pe.Sum(p => p.Valor);
                    bool apagado = F.Empresa != null && F.Empresa != emp;
                    Color c = Tema.CorEmpresa(emp, i);
                    if (apagado) c = Color.FromArgb(70, c);
                    float w = somaBase > 0 ? (float)(soma / somaBase * bw) : 0;
                    if (w >= 1)
                    {
                        using (var b = new SolidBrush(c)) g.FillRectangle(b, bx, by, Math.Max(1, w - 2), bh);
                        alvos.Add(new KeyValuePair<RectangleF, string>(new RectangleF(bx, by - 4, w, bh + 8), emp));
                    }
                    bx += w;
                    // legenda
                    float lx = x + i * colW, ly = by + 18;
                    Color txt = apagado ? Tema.TextoFraco : Tema.Texto, sec = apagado ? Tema.TextoFraco : Tema.TextoSec;
                    using (var b = new SolidBrush(c)) g.FillEllipse(b, lx, ly + 4, 9, 9);
                    string pct = somaBase > 0 ? (soma / somaBase * 100).ToString("0.0", Tema.BR) + "%" : "";
                    using (var f = Tema.Fonte(8.75f, F.Empresa == emp)) using (var b = new SolidBrush(F.Empresa == emp ? Tema.Destaque : sec))
                        g.DrawString(emp + "  ·  " + pct, f, b, lx + 13, ly);
                    string vs = Tema.Reais(soma);
                    float wvs;
                    using (var f = Tema.Fonte(11f, true)) { wvs = g.MeasureString(vs, f).Width; using (var b = new SolidBrush(txt)) g.DrawString(vs, f, b, lx - 2, ly + 16); }
                    if (comparar && tela.TemAnteriorDe(emp))
                    {
                        Color cv; string v = Desenho.Variacao(soma, baseAnt.Where(p => p.Empresa == emp).Sum(p => p.Valor), out cv);
                        if (v != null) using (var f = Tema.Fonte(8.75f, true)) using (var b = new SolidBrush(apagado ? Tema.TextoFraco : cv)) g.DrawString(v, f, b, lx + wvs + 6, ly + 19);
                    }
                    alvos.Add(new KeyValuePair<RectangleF, string>(new RectangleF(lx - 4, ly - 2, colW - 8, 40), emp));
                }
            }

            // cartão grande de Licitação
            if (temLic)
            {
                var rl = new RectangleF(Width - wLic + 0.5f, 0.5f, wLic - 1.5f, Height - 1.5f);
                Cartao(g, rl);
                Color roxo = Tema.Cor("#6250D6");
                using (var b = new SolidBrush(roxo)) using (var path = Desenho.Arredondado(new RectangleF(rl.X, rl.Y, 5, rl.Height), 2)) g.FillPath(b, path);
                var lic = filt.Where(p => p.Licitacao).ToList();
                double l = lic.Sum(p => p.Valor);
                float lx = rl.X + 24;
                using (var f = Tema.Fonte(9.75f, true)) using (var b = new SolidBrush(roxo)) g.DrawString("●  Licitação (dentro do total)", f, b, lx, 14);
                using (var f = Tema.Fonte(21f, true)) using (var b = new SolidBrush(Tema.Texto)) g.DrawString(Tema.Reais(l), f, b, lx - 3, 36);
                using (var f = Tema.Fonte(9.25f)) using (var b = new SolidBrush(Tema.TextoSec))
                    g.DrawString((tot > 0 ? (l / tot * 100).ToString("0.0", Tema.BR) + "% do total · " : "") + lic.Count + " " + tela.Itens + " · " + TelaPainel.NCli(lic.Select(Filtros.ChaveC).Distinct().Count()), f, b, lx, 80);
                using (var f = new Font("Segoe UI", 9.25f, FontStyle.Underline)) using (var b = new SolidBrush(roxo))
                    g.DrawString(F.SoLicitacao ? "✓ mostrando só licitação — tirar" : "ver só licitação  →", f, b, lx, 112);
                alvoLic = rl;
            }
        }
    }

    // ------------------------------------------------------------------
    // Cabeçalho do corpo: abas "Vendedores | Clientes", Ranking/Por empresa
    // e o caminho ("Todos os vendedores › FERNANDA › CLIENTE") para voltar.
    // ------------------------------------------------------------------
    class CabCorpo : Control
    {
        readonly TelaPainel tela;
        readonly List<KeyValuePair<RectangleF, Action>> alvos = new List<KeyValuePair<RectangleF, Action>>();

        public CabCorpo(TelaPainel t)
        {
            tela = t;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Tema.Cartao;
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); Cursor = alvos.Any(a => a.Key.Contains(e.Location)) ? Cursors.Hand : Cursors.Default; }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            foreach (var a in alvos) if (a.Key.Contains(e.Location)) { a.Value(); return; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Tema.Cartao);
            alvos.Clear();
            float x = 0;
            string[] abas = { "Vendedores", "Clientes" };
            for (int i = 0; i < 2; i++)
            {
                bool on = tela.Aba == i;
                using (var f = Tema.Fonte(12.5f, true))
                {
                    float w = g.MeasureString(abas[i], f).Width;
                    using (var b = new SolidBrush(on ? Tema.Texto : Tema.Cor("#A6ABB5"))) g.DrawString(abas[i], f, b, x, 10);
                    if (on) using (var b = new SolidBrush(Tema.Destaque)) g.FillRectangle(b, x + 3, 38, w - 6, 3);
                    int k = i;
                    if (!on || tela.ClienteAberto != null || tela.VendAberto != null) alvos.Add(new KeyValuePair<RectangleF, Action>(new RectangleF(x, 4, w, 38), () => tela.IrAba(k)));
                    x += w + 18;
                }
            }
            using (var p = new Pen(Tema.Grade)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);

            // Ranking | Por empresa (só na aba Vendedores)
            float xr = Width;
            if (tela.Aba == 0)
            {
                using (var f = Tema.Fonte(9f))
                {
                    float w1 = g.MeasureString("Ranking", f).Width + 20, w2 = g.MeasureString("Por empresa", f).Width + 20;
                    var r2 = new RectangleF(Width - w2 - 1, 11, w2, 26); var r1 = new RectangleF(r2.X - w1, 11, w1, 26);
                    foreach (var par in new[] { new { R = r1, T = "Ranking", A = !tela.ModoPorEmpresa, V = false }, new { R = r2, T = "Por empresa", A = tela.ModoPorEmpresa, V = true } })
                    {
                        using (var bg = new SolidBrush(par.A ? Tema.Destaque : Tema.Cartao)) g.FillRectangle(bg, par.R);
                        using (var p = new Pen(par.A ? Tema.Destaque : Tema.Borda)) g.DrawRectangle(p, par.R.X, par.R.Y, par.R.Width, par.R.Height);
                        using (var fb = Tema.Fonte(9f, par.A)) using (var b = new SolidBrush(par.A ? Color.White : Tema.Texto))
                            g.DrawString(par.T, fb, b, par.R, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                        bool v = par.V;
                        alvos.Add(new KeyValuePair<RectangleF, Action>(par.R, () => tela.TrocarModo(v)));
                    }
                    xr = r1.X - 20;
                }
            }
            else if (tela.ClienteAberto == null)
            {
                // "Clientes de licitação" (lista)
                using (var f = new Font("Segoe UI", 9.25f, FontStyle.Underline))
                {
                    string t = "Clientes de licitação";
                    float w = g.MeasureString(t, f).Width;
                    var r = new RectangleF(Width - w - 2, 14, w, 20);
                    using (var b = new SolidBrush(Tema.Cor("#6250D6"))) g.DrawString(t, f, b, r.X, r.Y);
                    alvos.Add(new KeyValuePair<RectangleF, Action>(r, () => tela.PedirListaLic()));
                    xr = r.X - 20;
                }
            }
            else xr = Width - 240;   // espaço do botão de licitação

            // caminho (migalhas) ou dica, entre as abas e a direita
            var itens = new List<KeyValuePair<string, Action>>();
            if (tela.Aba == 0)
            {
                if (tela.VendAberto != null) { itens.Add(new KeyValuePair<string, Action>("Todos os vendedores", () => tela.Voltar(0))); itens.Add(new KeyValuePair<string, Action>(tela.NomeVend(tela.VendAberto), null)); }
            }
            else if (tela.ClienteAberto != null)
            {
                if (tela.VendOrigem != null)
                {
                    itens.Add(new KeyValuePair<string, Action>("Todos os vendedores", () => tela.Voltar(0)));
                    itens.Add(new KeyValuePair<string, Action>(tela.NomeVend(tela.VendOrigem), () => tela.Voltar(1)));
                }
                else itens.Add(new KeyValuePair<string, Action>("Todos os clientes", () => tela.Voltar(0)));
                itens.Add(new KeyValuePair<string, Action>(tela.NomeCliente(tela.ClienteAberto), null));
            }
            string dica = tela.Aba == 0 ? (tela.VendAberto == null ? "clique num vendedor para ver os clientes dele" : "")
                                        : tela.InfoClientes;
            using (var fsec = Tema.Fonte(9.25f))
            {
                float xs = x + 14;
                if (itens.Count > 0)
                {
                    for (int i = 0; i < itens.Count; i++)
                    {
                        bool ult = i == itens.Count - 1;
                        using (var f = Tema.Fonte(9.75f, ult))
                        {
                            float w = Math.Min(g.MeasureString(itens[i].Key, f).Width, Math.Max(40, xr - xs));
                            var r = new RectangleF(xs, 15, w, 20);
                            using (var b = new SolidBrush(ult ? Tema.Texto : Tema.Destaque))
                                g.DrawString(itens[i].Key, f, b, r, new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                            if (itens[i].Value != null) alvos.Add(new KeyValuePair<RectangleF, Action>(r, itens[i].Value));
                            xs += w + 4;
                        }
                        if (!ult) using (var b = new SolidBrush(Tema.TextoFraco)) { g.DrawString("›", fsec, b, xs, 15); xs += 14; }
                    }
                    if (tela.Aba == 1 && dica != "")
                        using (var b = new SolidBrush(Tema.TextoSec))
                            g.DrawString("·  " + dica, fsec, b, new RectangleF(xs + 6, 16, Math.Max(10, xr - xs - 6), 20), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                }
                else if (dica != "")
                    using (var b = new SolidBrush(Tema.TextoSec))
                        g.DrawString(dica, fsec, b, new RectangleF(xs, 16, Math.Max(10, xr - xs), 20), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
            }
        }
    }

    // ------------------------------------------------------------------
    // Lista de vendedores: 1 linha por vendedor. Clicou, os clientes dele
    // abrem ali embaixo (sem pular para outro lugar). Detalhe no mouse.
    // "Por empresa": tabela Vendedor × Empresa.
    // ------------------------------------------------------------------
    class ListaVend : Control
    {
        readonly TelaPainel tela;
        readonly List<KeyValuePair<RectangleF, Action>> alvos = new List<KeyValuePair<RectangleF, Action>>();
        readonly Dictionary<RectangleF, string> dicas = new Dictionary<RectangleF, string>();
        RectangleF hover = RectangleF.Empty;
        string dicaAtual;
        const int ALT = 40, ALT_CLI = 28, MAX_CLI = 8;

        public ListaVend(TelaPainel t)
        {
            tela = t;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Tema.Cartao;
        }

        class Linha
        {
            public string Chave, Nome, Principal;
            public double Total, Anterior, Comp;
            public List<Pedido> Peds;
            public Dictionary<string, double> PorEmpresa;
        }

        List<Linha> Linhas(out List<Pedido> ant, out List<string> empresas)
        {
            // todos os filtros, menos o de vendedor (a lista é dos vendedores)
            var peds = tela.Filtrar(tela.Todos, false).ToList();
            var antL = tela.Filtrar(tela.TodosAnteriores, false).ToList();
            ant = antL;
            var emps = tela.F.Empresa != null ? new List<string> { tela.F.Empresa } : tela.Empresas;
            empresas = emps;
            var sel = tela.F.Vendedores;
            return peds.GroupBy(Filtros.ChaveV).Where(gr => sel.Count == 0 || sel.Contains(gr.Key)).Select(gr =>
            {
                var cli = gr.GroupBy(Filtros.ChaveC).OrderByDescending(c => c.Sum(p => p.Valor)).First();
                double tot = gr.Sum(p => p.Valor);
                return new Linha
                {
                    Chave = gr.Key, Nome = gr.First().Vendedor.Nome, Total = tot, Peds = gr.ToList(), Comp = tela.Comparavel(gr),
                    Principal = cli.First().Cliente + " (" + (tot > 0 ? cli.Sum(p => p.Valor) / tot * 100 : 0).ToString("0", Tema.BR) + "%)",
                    Anterior = antL.Where(p => Filtros.ChaveV(p) == gr.Key).Sum(p => p.Valor),
                    PorEmpresa = emps.ToDictionary(x => x, x => gr.Where(p => p.Empresa == x).Sum(p => p.Valor))
                };
            }).OrderByDescending(l => l.Total).ToList();
        }

        int AlturaBloco(Linha l)
        {
            int n = l.Peds.Select(Filtros.ChaveC).Distinct().Count();
            return 12 + 24 + Math.Min(n, MAX_CLI) * ALT_CLI + (n > MAX_CLI ? ALT_CLI : 0) + 8;
        }

        public int AlturaNecessaria(int largura)
        {
            List<Pedido> ant; List<string> emps;
            var ls = Linhas(out ant, out emps);
            if (tela.ModoPorEmpresa) return 34 + ls.Count * 36 + 8;
            int h = ls.Count * ALT + 8;
            var ab = ls.FirstOrDefault(l => l.Chave == tela.VendAberto);
            if (ab != null) h += AlturaBloco(ab);
            return Math.Max(h, 60);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = alvos.Any(a => a.Key.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
            var h = dicas.Keys.FirstOrDefault(r => r.Contains(e.Location));
            if (h != hover) { hover = h; Invalidate(); }
            string d = h.IsEmpty ? null : dicas[h];
            if (d != dicaAtual) { dicaAtual = d; tela.Dica.SetToolTip(this, d ?? ""); }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = RectangleF.Empty; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            foreach (var a in alvos) if (a.Key.Contains(e.Location)) { a.Value(); return; }
        }

        static readonly StringFormat Corta = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };
        static readonly StringFormat Dir = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Tema.Cartao);
            alvos.Clear(); dicas.Clear();
            List<Pedido> ant; List<string> empresas;
            var linhas = Linhas(out ant, out empresas);
            if (linhas.Count == 0)
            {
                using (var f = Tema.Fonte(10f)) using (var b = new SolidBrush(Tema.TextoFraco)) g.DrawString("Nenhuma venda com estes filtros.", f, b, 4, 16);
                return;
            }
            if (tela.ModoPorEmpresa) { PorEmpresa(g, linhas, empresas); return; }

            bool comparar = tela.TemAnterior;
            float W = Width;
            float colPos = 30, colNome = Math.Min(280, W * 0.22f), colValor = 130, colVar = 74;
            float xNome = colPos, xBarra = xNome + colNome + 12, xValor = W - colVar - colValor - 8, larg = xValor - xBarra - 16;
            double max = linhas.Max(l => l.Total);
            float y = 2;
            for (int i = 0; i < linhas.Count; i++)
            {
                var l = linhas[i];
                bool aberto = tela.VendAberto == l.Chave;
                var area = new RectangleF(0, y, W, ALT);
                if (aberto) using (var bg = new SolidBrush(Tema.DestaqueClaro)) g.FillRectangle(bg, area);
                else if (area == hover) using (var bg = new SolidBrush(Tema.Cor("#F7F7F5"))) g.FillRectangle(bg, area);
                using (var p = new Pen(Tema.Grade)) g.DrawLine(p, 0, y, W, y);
                using (var f = Tema.Fonte(9.5f, true)) using (var b = new SolidBrush(Tema.TextoFraco)) g.DrawString((i + 1).ToString(), f, b, new RectangleF(2, y, colPos - 4, ALT), Corta);
                using (var f = Tema.Fonte(10f, aberto)) using (var b = new SolidBrush(aberto ? Tema.Destaque : Tema.Texto))
                    g.DrawString(l.Nome + (aberto ? "  ▾" : ""), f, b, new RectangleF(xNome, y, colNome, ALT), Corta);
                // barra dividida por empresa
                float bx = xBarra, hb = 14, yc = y + ALT / 2f;
                for (int k = 0; k < empresas.Count; k++)
                {
                    double v = l.PorEmpresa[empresas[k]];
                    float wk = (float)(v / max * larg);
                    if (wk < 0.5f) continue;
                    using (var b = new SolidBrush(Tema.CorEmpresa(empresas[k], tela.Empresas.IndexOf(empresas[k])))) g.FillRectangle(b, bx, yc - hb / 2, Math.Max(1, wk - (empresas.Count > 1 ? 2 : 0)), hb);
                    bx += wk;
                }
                using (var f = Tema.Fonte(10f, true)) using (var b = new SolidBrush(Tema.Texto)) g.DrawString(Tema.Reais(l.Total), f, b, new RectangleF(xValor, y, colValor, ALT), Dir);
                if (comparar)
                {
                    Color cv; string v = Desenho.Variacao(l.Comp, l.Anterior, out cv);
                    if (v != null) using (var f = Tema.Fonte(9f, true)) using (var b = new SolidBrush(cv)) g.DrawString(v, f, b, new RectangleF(W - colVar - 4, y, colVar, ALT), Dir);
                }
                string chave = l.Chave;
                alvos.Add(new KeyValuePair<RectangleF, Action>(area, () => tela.AbrirVendedor(chave)));
                var porEmp = string.Join("  ·  ", empresas.Where(x => l.PorEmpresa[x] > 0.004).Select(x => x + " " + Tema.Reais(l.PorEmpresa[x])));
                dicas[area] = l.Peds.Count + " " + tela.Itens + " · " + TelaPainel.NCli(l.Peds.Select(Filtros.ChaveC).Distinct().Count()) + " · principal: " + l.Principal +
                              (empresas.Count > 1 ? "\n" + porEmp : "") + "\n" + (aberto ? "clique para fechar" : "clique para ver os clientes");
                y += ALT;

                if (aberto) y = Bloco(g, l, ant, empresas, y, xBarra, xValor, colValor, colVar, W);
            }
            using (var p = new Pen(Tema.Grade)) g.DrawLine(p, 0, y, W, y);
        }

        // Clientes do vendedor aberto, logo abaixo dele
        float Bloco(Graphics g, Linha l, List<Pedido> ant, List<string> empresas, float y, float x0, float xValor, float colValor, float colVar, float W)
        {
            float h = AlturaBloco(l);
            using (var bg = new SolidBrush(Tema.Cor("#FAFBFC"))) g.FillRectangle(bg, 0, y, W, h);
            using (var p = new Pen(Tema.Cor("#DCE8F8"))) { g.DrawLine(p, 0, y, W, y); g.DrawLine(p, 0, y + h, W, y + h); }
            var clientes = l.Peds.GroupBy(Filtros.ChaveC).Select(c => new
            {
                Chave = c.Key, Nome = c.GroupBy(p => p.Cliente).OrderByDescending(x => x.Count()).First().Key,
                Valor = c.Sum(p => p.Valor), N = c.Count(), Lic = c.Any(p => p.Licitacao), Comp = tela.Comparavel(c),
                Ant = ant.Where(p => Filtros.ChaveV(p) == l.Chave && Filtros.ChaveC(p) == c.Key).Sum(p => p.Valor)
            }).OrderByDescending(c => c.Valor).ToList();
            float yy = y + 10;
            string info = l.Peds.Count + " " + tela.Itens + " · " + TelaPainel.NCli(clientes.Count) +
                          (empresas.Count > 1 ? "  ·  " + string.Join("  ·  ", empresas.Where(x => l.PorEmpresa[x] > 0.004).Select(x => x + " " + Tema.Reais(l.PorEmpresa[x]))) : "");
            using (var f = Tema.Fonte(9f)) using (var b = new SolidBrush(Tema.TextoSec)) g.DrawString(info, f, b, new RectangleF(x0, yy, W - x0 - 10, 22), Corta);
            yy += 24;
            float colPct = 54, xPct = xValor - colPct - 6;
            foreach (var c in clientes.Take(MAX_CLI))
            {
                var area = new RectangleF(x0 - 8, yy, W - x0, ALT_CLI);
                if (area == hover) using (var bg = new SolidBrush(Tema.Cor("#EEF3FA"))) g.FillRectangle(bg, area);
                Color cor = c.Lic ? Tema.Cor("#6250D6") : Tema.Texto;
                string nome = c.Nome + (c.Lic ? "  (licitação)" : "");
                float wn;
                using (var f = Tema.Fonte(9.5f))
                {
                    wn = Math.Min(g.MeasureString(nome, f).Width, xPct - x0 - 70);
                    using (var b = new SolidBrush(cor)) g.DrawString(nome, f, b, new RectangleF(x0, yy, wn + 2, ALT_CLI), Corta);
                }
                if (c.N > 1) using (var f = Tema.Fonte(8.5f)) using (var b = new SolidBrush(Tema.TextoFraco)) g.DrawString(c.N + " " + tela.ItensCurto, f, b, new RectangleF(x0 + wn + 8, yy, 60, ALT_CLI), Corta);
                using (var f = Tema.Fonte(9f)) using (var b = new SolidBrush(Tema.TextoSec))
                    g.DrawString((l.Total > 0 ? c.Valor / l.Total * 100 : 0).ToString("0", Tema.BR) + "%", f, b, new RectangleF(xPct, yy, colPct, ALT_CLI), Dir);
                using (var f = Tema.Fonte(9.5f)) using (var b = new SolidBrush(cor)) g.DrawString(Tema.Reais(c.Valor), f, b, new RectangleF(xValor, yy, colValor, ALT_CLI), Dir);
                if (c.Ant > 0.005)
                {
                    Color cv; string v = Desenho.Variacao(c.Comp, c.Ant, out cv);
                    if (v != null) using (var f = Tema.Fonte(8.75f, true)) using (var b = new SolidBrush(cv)) g.DrawString(v, f, b, new RectangleF(W - colVar - 4, yy, colVar, ALT_CLI), Dir);
                }
                string ck = c.Chave, vk = l.Chave;
                alvos.Add(new KeyValuePair<RectangleF, Action>(area, () => tela.AbrirClienteDoVendedor(ck, vk)));
                dicas[area] = "clique para ver os " + tela.Itens + " deste cliente";
                yy += ALT_CLI;
            }
            if (clientes.Count > MAX_CLI)
            {
                string t = "+ " + (clientes.Count - MAX_CLI) + " clientes — ver todos os clientes de " + TelaPainel.PrimeiroNome(l.Nome);
                using (var f = new Font("Segoe UI", 9.25f, FontStyle.Underline))
                {
                    float w = g.MeasureString(t, f).Width;
                    var r = new RectangleF(x0, yy + 4, w, 20);
                    using (var b = new SolidBrush(Tema.Destaque)) g.DrawString(t, f, b, r.X, r.Y);
                    string vk = l.Chave;
                    alvos.Add(new KeyValuePair<RectangleF, Action>(r, () => tela.VerTodosClientesDe(vk)));
                }
            }
            return y + h;
        }

        // Vendedor × Empresa: cor mais forte onde vendeu mais
        void PorEmpresa(Graphics g, List<Linha> linhas, List<string> empresas)
        {
            bool comparar = tela.TemAnterior;
            var cols = new List<string>(empresas); cols.Add("Total"); if (comparar) cols.Add("vs ant.");
            float W = Width, colNome = Math.Min(260, W * 0.26f), cw = (W - 8 - colNome) / cols.Count, alt = 36, topo = 4;
            using (var f = Tema.Fonte(9.25f, true)) using (var b = new SolidBrush(Tema.TextoSec))
            {
                g.DrawString("Vendedor", f, b, 30, topo + 6);
                for (int c = 0; c < cols.Count; c++) g.DrawString(cols[c], f, b, new RectangleF(colNome + c * cw, topo + 6, cw - 8, 20), new StringFormat { Alignment = StringAlignment.Far });
            }
            using (var p = new Pen(Tema.Borda)) g.DrawLine(p, 0, topo + 30, W, topo + 30);
            double maxCel = linhas.SelectMany(l => l.PorEmpresa.Values).DefaultIfEmpty(1).Max();
            float y0 = topo + 32;
            for (int i = 0; i < linhas.Count; i++)
            {
                var l = linhas[i];
                float y = y0 + alt * i;
                var area = new RectangleF(0, y, W, alt);
                if (area == hover) using (var bg = new SolidBrush(Tema.Cor("#F7F7F5"))) g.FillRectangle(bg, area);
                using (var f = Tema.Fonte(9.5f, true)) using (var b = new SolidBrush(Tema.TextoFraco)) g.DrawString((i + 1).ToString(), f, b, new RectangleF(2, y, 26, alt), Corta);
                using (var f = Tema.Fonte(9.75f)) using (var b = new SolidBrush(Tema.Texto)) g.DrawString(l.Nome, f, b, new RectangleF(30, y, colNome - 30, alt), Corta);
                for (int c = 0; c < empresas.Count; c++)
                {
                    double v = l.PorEmpresa[empresas[c]];
                    var cel = new RectangleF(colNome + c * cw + 4, y + 4, cw - 8, alt - 8);
                    if (v > 0)
                    {
                        int alfa = (int)(25 + 150 * v / maxCel);
                        using (var bg = new SolidBrush(Color.FromArgb(alfa, Tema.Destaque))) using (var path = Desenho.Arredondado(cel, 4)) g.FillPath(bg, path);
                    }
                    using (var f = Tema.Fonte(9.5f)) using (var b = new SolidBrush(v / maxCel > 0.6 ? Color.White : Tema.Texto))
                        g.DrawString(v > 0 ? Tema.Reais(v) : "—", f, b, new RectangleF(cel.X, cel.Y, cel.Width - 6, cel.Height), Dir);
                }
                using (var f = Tema.Fonte(9.75f, true)) using (var b = new SolidBrush(Tema.Texto))
                    g.DrawString(Tema.Reais(l.Total), f, b, new RectangleF(colNome + empresas.Count * cw, y, cw - 8, alt), Dir);
                if (comparar)
                {
                    Color cv; string v = Desenho.Variacao(l.Comp, l.Anterior, out cv);
                    using (var f = Tema.Fonte(8.75f, true)) using (var b = new SolidBrush(cv))
                        g.DrawString(v ?? "", f, b, new RectangleF(colNome + (empresas.Count + 1) * cw, y, cw - 8, alt), Dir);
                }
                using (var p = new Pen(Tema.Grade)) g.DrawLine(p, 0, y + alt, W, y + alt);
                string chave = l.Chave;
                alvos.Add(new KeyValuePair<RectangleF, Action>(area, () => tela.AbrirVendedor(chave)));
                dicas[area] = "clique para ver os clientes de " + l.Nome;
            }
        }
    }
}
