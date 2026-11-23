// =====================================================================
//  Dialogos.cs  -  Janelinhas de LICITAÇÃO
//   - DialogoRegras: ver e desmarcar os clientes marcados como licitação
// =====================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PainelPedidos
{
    class DialogoRegras : Form
    {
        readonly DataGridView grade = new DataGridView();
        public List<RegraLicitacao> Regras { get; private set; }

        public DialogoRegras(List<RegraLicitacao> regras)
        {
            Regras = regras.Select(r => new RegraLicitacao { Cliente = r.Cliente }).ToList();
            Text = "Clientes de licitação";
            Font = Tema.Fonte(9.75f);
            BackColor = Tema.Cartao;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(620, 440);
            MinimumSize = new Size(520, 300);
            ShowInTaskbar = false;
            Padding = new Padding(16);

            var topo = new Label
            {
                Dock = DockStyle.Top, Height = 46, ForeColor = Tema.TextoSec,
                Text = "Toda venda para estas razões sociais fica marcada como licitação (continua no vendedor do PDF).\n" +
                       "Arquivo: " + Licitacoes.Arquivo
            };
            Tema.EstilizarGrade(grade);
            grade.Dock = DockStyle.Fill;
            Tema.Coluna(grade, "Cliente (razão social)", 160);

            var baixo = new Panel { Dock = DockStyle.Bottom, Height = 50 };
            var remover = new Button { Text = "Desmarcar selecionado", AutoSize = true, FlatStyle = FlatStyle.System, Location = new Point(0, 14) };
            var ok = new Button { Text = "Salvar", AutoSize = true, FlatStyle = FlatStyle.System, DialogResult = DialogResult.OK, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            var cancelar = new Button { Text = "Cancelar", AutoSize = true, FlatStyle = FlatStyle.System, DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            remover.Click += delegate
            {
                if (grade.SelectedRows.Count == 0 || grade.SelectedRows[0].Tag == null) return;
                Regras.Remove((RegraLicitacao)grade.SelectedRows[0].Tag);
                Preencher();
            };
            baixo.Controls.AddRange(new Control[] { remover, ok, cancelar });
            baixo.Resize += delegate
            {
                cancelar.Location = new Point(baixo.Width - cancelar.Width, 14);
                ok.Location = new Point(cancelar.Left - ok.Width - 8, 14);
            };
            AcceptButton = ok; CancelButton = cancelar;

            Controls.Add(grade); Controls.Add(baixo); Controls.Add(topo);
            Preencher();
        }

        void Preencher()
        {
            grade.Rows.Clear();
            foreach (var r in Regras.OrderBy(x => x.Cliente))
            {
                int i = grade.Rows.Add(r.Cliente);
                grade.Rows[i].Tag = r;
            }
            if (Regras.Count == 0) grade.Rows.Add("(nenhum cliente marcado como licitação)");
            grade.ClearSelection();
        }
    }
}
