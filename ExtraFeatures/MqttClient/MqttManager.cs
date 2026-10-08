using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using opentuner.MediaSources;
using opentuner.Utilities;
using Serilog;

namespace opentuner.ExtraFeatures.MqttClient
{
    public delegate void NewMqttMessage(MqttMessage Message);
    
    public class MqttManager
    {
        private string _broker;
        private int _broker_port;
        private string _clientid;
        private string _maintopic = "dt/opentuner/";
        private string _cmdtopic = "cmd/opentuner/";

        private IMqttClient _mqtt_client;
        private MqttClientOptions _options;

        private readonly object _reconnect_lock = new object();
        private bool _reconnecting = false;
        private bool _disposed = false;
        private int _reconnect_attempt = 0;

        public event NewMqttMessage OnMqttMessageReceived;

        public bool IsConnected => _mqtt_client != null && _mqtt_client.IsConnected;

        private MqttManagerSettings _settings;
        private SettingsManager<MqttManagerSettings> _settingsManager;

        public MqttManager() 
        {
            _settings = new MqttManagerSettings();
            _settingsManager = new SettingsManager<MqttManagerSettings>("mqttclient_settings");
            _settings = _settingsManager.LoadSettings(_settings);

            _broker = _settings.MqttBroker;
            _broker_port = _settings.MqttPort;

            _clientid = "OT" + Guid.NewGuid().ToString();

            // client factory
            var factory = new MqttFactory();

            // client instance
            _mqtt_client = factory.CreateMqttClient();

            // client options
            _options = new MqttClientOptionsBuilder()
                .WithTcpServer(_broker, _broker_port)
                .WithClientId(_clientid)
                .WithCleanSession()
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(20))
                .Build();

            _mqtt_client.ConnectedAsync += _mqtt_client_ConnectedAsync;
            _mqtt_client.DisconnectedAsync += _mqtt_client_DisconnectedAsync;
            _mqtt_client.ApplicationMessageReceivedAsync += _mqtt_client_ApplicationMessageReceivedAsync;

            Connect();
        }

        public void Connect()
        {
            Task.Run(async () =>
            {
                try
                {
                    if (!_mqtt_client.IsConnected)
                    {
                        await _mqtt_client.ConnectAsync(_options);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning("Mqtt connect failed: " + ex.Message);
                    StartReconnectLoop();
                }
            });
        }

        public void Disconnect()
        {
            _disposed = true;
            try
            {
                _mqtt_client.DisconnectAsync();
            }
            catch (Exception ex)
            {
            }
        }

        private void StartReconnectLoop()
        {
            lock (_reconnect_lock)
            {
                if (_reconnecting || _disposed)
                    return;

                _reconnecting = true;
            }

            Task.Run(async () =>
            {
                try
                {
                    while (!_disposed && !_mqtt_client.IsConnected)
                    {
                        // exponential backoff capped at 30s
                        int delay = Math.Min(30, (int)Math.Pow(2, Math.Min(_reconnect_attempt, 5)));
                        _reconnect_attempt++;

                        Log.Information("Mqtt reconnecting in " + delay + "s...");

                        for (int i = 0; i < delay && !_disposed; i++)
                            await Task.Delay(1000);

                        if (_disposed)
                            break;

                        try
                        {
                            await _mqtt_client.ConnectAsync(_options);
                        }
                        catch (Exception ex)
                        {
                            Log.Warning("Mqtt reconnect attempt failed: " + ex.Message);
                        }
                    }
                }
                finally
                {
                    lock (_reconnect_lock) { _reconnecting = false; }
                }
            });
        }

        public void SendProperties(OTSourceData properties, string ChildTopic)
        {
            SendMqttStatus(ChildTopic + "/demod_locked", properties.demod_locked.ToString());
            SendMqttStatus(ChildTopic + "/frequency", properties.frequency.ToString());
            SendMqttStatus(ChildTopic + "/symbol_rate", properties.symbol_rate.ToString());
            SendMqttStatus(ChildTopic + "/service_name", properties.service_name.ToString());
            SendMqttStatus(ChildTopic + "/mer", properties.mer.ToString());
            SendMqttStatus(ChildTopic + "/db_margin", properties.db_margin.ToString());
        }

        public void SendMqttStatus(string topic, string value)
        {
            if (!IsConnected)
                return;

            var message = new MqttApplicationMessageBuilder()
            .WithTopic(_maintopic + topic)
            .WithPayload(value)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

            Task.Run(async () =>
            {
                try { await _mqtt_client.PublishAsync(message); }
                catch (Exception ex) { Log.Warning("Mqtt publish failed: " + ex.Message); }
            });
        }

        // this requires a full topic - currently only used for pluto commands
        public async Task SendMqttCommand(string topic, string value)
        {
            if (!IsConnected)
                return;

            var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(value)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

            await _mqtt_client.PublishAsync(message);

            return;
        }


        private Task _mqtt_client_ApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs arg)
        {
            OnMqttMessageReceived?.Invoke(new MqttMessage(arg.ApplicationMessage.Topic, arg.ApplicationMessage.ConvertPayloadToString()));
            return Task.CompletedTask;
        }

        private Task _mqtt_client_DisconnectedAsync(MqttClientDisconnectedEventArgs arg)
        {
            Log.Information("Mqtt Disconnected");
            if (!_disposed)
                StartReconnectLoop();
            return Task.CompletedTask;
        }

        private async Task _mqtt_client_ConnectedAsync(MqttClientConnectedEventArgs arg)
        {
            Log.Information("Mqtt Connected");
            _reconnect_attempt = 0;

            // subscribe to mqtt commands
            try
            {
                await _mqtt_client.SubscribeAsync(_cmdtopic + "tuner1/#");
                await _mqtt_client.SubscribeAsync(_cmdtopic + "tuner2/#");
            }
            catch (Exception ex)
            {
                Log.Warning("Mqtt subscribe failed: " + ex.Message);
            }

            return;
        }
    }
}
