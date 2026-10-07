using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace opentuner.MediaSources.Minitiouner.HardwareInterfaces
{
    public abstract class MTHardwareInterface
    {
        public MTHardwareInterface() { }

        public abstract bool RequireSerialTS { get;  }

        public abstract string GetName { get; }

        // Timestamp of the last successful communication with the device. Used by
        // the owning source to detect a lost connection and trigger a reconnect.
        public DateTime LastSuccessfulIo { get; protected set; } = DateTime.UtcNow;

        // True once the device has been successfully opened/initialised.
        public bool IsConnected { get; protected set; } = false;

        public event EventHandler<bool> ConnectionStatusChanged;

        protected void MarkIoSuccess()
        {
            LastSuccessfulIo = DateTime.UtcNow;

            if (!IsConnected)
            {
                IsConnected = true;
                ConnectionStatusChanged?.Invoke(this, true);
            }
        }

        protected void MarkIoFailure()
        {
            if (IsConnected)
            {
                IsConnected = false;
                ConnectionStatusChanged?.Invoke(this, false);
            }
        }

        // Called by the source when it (re)initialises the hardware.
        public void MarkConnected()
        {
            MarkIoSuccess();
        }

        public void MarkDisconnected()
        {
            IsConnected = false;
        }

        // TODO: change to more generic detect
        public abstract byte hw_detect(ref uint i2c_port, ref uint ts_port, ref uint ts_port2, ref string detectedDeviceName, string i2c_serial, string ts_serial, string ts2_serial);
        public abstract byte hw_detect(ref uint i2c_port, ref uint ts_port, ref uint ts_port2, ref string detectedDeviceName);

        // TODO: change to more generic init
        public abstract byte hw_init(uint i2c_device, uint ts_device, uint ts_device2);

        public abstract void hw_close();

        public abstract byte hw_set_polarization_supply(byte lnb_num, bool supply_enable, bool supply_horizontal);
        public abstract byte hw_ts_led(int led, bool setting);


        public abstract byte transport_flush(int device);
        public abstract byte transport_read(int device, ref byte[] data, ref uint bytesRead);

        public abstract byte nim_read_reg8(byte addr, byte reg, ref byte val);
        public abstract byte nim_write_reg8(byte addr, byte reg, byte val);
        public abstract byte nim_write_reg16(byte addr, ushort reg, byte val);
        public abstract byte nim_read_reg16(byte addr, ushort reg, ref byte val);
    }
}
