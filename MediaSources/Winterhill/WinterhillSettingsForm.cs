using System;
using System.Windows.Forms;

namespace opentuner.MediaSources.WinterHill
{
    public partial class WinterHillSettingsForm : Form
    {
        private WinterHillSettings _settings;
        private PicoWHBroadcastListenerForm _broadcastListenerForm;

        public WinterHillSettingsForm(WinterHillSettings Settings)
        {
            InitializeComponent();

            opentuner.Utilities.Theme.Apply(this);

            _settings = Settings;

            comboDefaultInterface.SelectedIndex = _settings.DefaultInterface;
            txtWHWSIp.Text = _settings.WinterHillWSHost;
            txtWHWSPort.Text = _settings.WinterHillWSPort.ToString();
            txtWHWSBaseUdp.Text = _settings.WinterHillWSUdpBasePort.ToString();

            txtUDPBasePort.Text = _settings.WinterHillUdpBasePort.ToString();
            txtUDPIP.Text = _settings.WinterHillUdpHost.ToString();

            checkAutoReconnect.Checked = _settings.AutoReconnect;
            checkAutoFind.Checked = _settings.AutoFindUdp;
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            int wsport = 0;
            int baseport = 0;

            int udpbaseport = 0;

            if (!int.TryParse(txtWHWSPort.Text, out wsport))
            {
                MessageBox.Show("WinterHill WS Port is not valid.");
                return;
            }

            if (!int.TryParse(txtWHWSBaseUdp.Text, out baseport))
            {
                MessageBox.Show("WinterHill WS Base Port is not valid");
                return;
            }

            if (!int.TryParse(txtUDPBasePort.Text, out udpbaseport))
            {
                MessageBox.Show("WinterHill UDP Base Port is not valid");
                return;
            }

            _settings.WinterHillWSHost = txtWHWSIp.Text;
            _settings.WinterHillWSPort = wsport;
            _settings.WinterHillWSUdpBasePort = baseport;

            _settings.WinterHillUdpBasePort = udpbaseport;
            _settings.WinterHillUdpHost = txtUDPIP.Text;
            _settings.DefaultInterface = (byte)comboDefaultInterface.SelectedIndex;
            _settings.AutoReconnect = checkAutoReconnect.Checked;
            _settings.AutoFindUdp = checkAutoFind.Checked;

            DialogResult = DialogResult.OK;
            Close();
        }

        private void comboDefaultInterface_SelectedIndexChanged(object sender, EventArgs e)
        {
           
        }

        private void btnBroadcastListener_Click(object sender, EventArgs e)
        {
            // Reuse a single window so repeated clicks don't try to bind the
            // broadcast port a second time.
            if (_broadcastListenerForm == null || _broadcastListenerForm.IsDisposed)
            {
                _broadcastListenerForm = new PicoWHBroadcastListenerForm();
            }

            _broadcastListenerForm.Show();
            _broadcastListenerForm.BringToFront();
            _broadcastListenerForm.Activate();
        }
    }
}
