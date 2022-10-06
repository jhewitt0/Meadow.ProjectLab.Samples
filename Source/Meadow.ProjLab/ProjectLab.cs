using Meadow.Foundation.Audio;
using Meadow.Foundation.Displays;
using Meadow.Foundation.ICs.IOExpanders;
using Meadow.Foundation.Leds;
using Meadow.Foundation.Sensors.Accelerometers;
using Meadow.Foundation.Sensors.Atmospheric;
using Meadow.Foundation.Sensors.Buttons;
using Meadow.Foundation.Sensors.Light;
using Meadow.Hardware;
using Meadow.Logging;
using Meadow.Units;
using System;

namespace Meadow.Devices
{
    public class ProjectLab
    {
        protected Logger? Logger { get; } = Resolver.Log;
        public ISpiBus SpiBus { get; }
        public II2cBus I2CBus { get; }

        private readonly Lazy<RgbPwmLed> _led;
        private readonly Lazy<St7789> _display;
        private readonly Lazy<Bh1750> _lightSensor;
        private readonly Lazy<PushButton> _upButton;
        private readonly Lazy<PushButton> _downButton;
        private readonly Lazy<PushButton> _leftButton;
        private readonly Lazy<PushButton> _rightButton;
        private readonly Lazy<Bme680> _bme680;
        private readonly Lazy<PiezoSpeaker> _speaker;
        private readonly Lazy<Bmi270> _imu;

        public RgbPwmLed Led => _led.Value;
        public St7789 Display => _display.Value;
        public Bh1750 LightSensor => _lightSensor.Value;
        public PushButton UpButton => _upButton.Value;
        public PushButton DownButton => _downButton.Value;
        public PushButton LeftButton => _leftButton.Value;
        public PushButton RightButton => _rightButton.Value;
        public Bme680 EnvironmentalSensor => _bme680.Value;
        public PiezoSpeaker Speaker => _speaker.Value;
        public Bmi270 IMU => _imu.Value;

        private IProjectLabHardware _hardware;
        private string? _rev;

        public Mcp23008? Mcp_1 { get; }
        public Mcp23008? Mcp_2 { get; }
        public Mcp23008? Mcp_Version { get; }

        public ProjectLab()
        {
            // check to see if we have an MCP23008 - it was introduced in v2 hardware
            if (Resolver.Device == null)
            {
                var msg = "ProjLab instance must be created no earlier than App.Initialize()";
                Logger?.Error(msg);
                throw new Exception(msg);
            }

            var device = Resolver.Device as IF7FeatherMeadowDevice;

            if (device == null)
            {
                var msg = "ProjLab Device must be an F7Feather";
                Logger?.Error(msg);
                throw new Exception(msg);
            }

            // create our busses
            Logger?.Info("Creating comms busses...");
            var config = new SpiClockConfiguration(
                           new Frequency(48000, Frequency.UnitType.Kilohertz),
                           SpiClockConfiguration.Mode.Mode3);

            SpiBus = Resolver.Device.CreateSpiBus(
                device.Pins.SCK,
                device.Pins.COPI,
                device.Pins.CIPO,
                config);

            I2CBus = device.CreateI2cBus();

            // determine hardware

            try
            {
                // MCP the First
                IDigitalInputPort mcp1_int = device.CreateDigitalInputPort(
                    device.Pins.D09, InterruptMode.EdgeRising, ResistorMode.InternalPullDown);
                IDigitalOutputPort mcp_Reset = device.CreateDigitalOutputPort(device.Pins.D14);

                Mcp_1 = new Mcp23008(I2CBus, address: 0x20, mcp1_int, mcp_Reset);
            }
            catch (Exception e)
            {
                Logger?.Warn($"ERR creating MCP1: {e.Message}");
            }
            try
            {
                // MCP the Second
                IDigitalInputPort mcp2_int = device.CreateDigitalInputPort(
                    device.Pins.D10, InterruptMode.EdgeRising, ResistorMode.InternalPullDown);
                Mcp_2 = new Mcp23008(I2CBus, address: 0x21, mcp2_int);
            }
            catch (Exception e)
            {
                Logger?.Warn($"ERR creating MCP2: {e.Message}");
            }
            try
            {
                Mcp_Version = new Mcp23008(I2CBus, address: 0x23);
                byte version = Mcp_Version.ReadFromPorts(Mcp23xxx.PortBank.A);
                Logger?.Info($"Project Lab version: {version}");
            }
            catch (Exception e)
            {
                Logger?.Warn($"ERR creating the MCP that has version information: {e.Message}");
            }

            if (Mcp_1 == null)
            {
                _hardware = new ProjectLabHardwareV1(device, SpiBus);
            }
            else
            {
                _hardware = new ProjectLabHardwareV2(Mcp_1, device, SpiBus);
            }

            // lazy load all components
            try
            {
                _led = new Lazy<RgbPwmLed>(() =>
                    new RgbPwmLed(
                    device: device,
                    redPwmPin: device.Pins.OnboardLedRed,
                    greenPwmPin: device.Pins.OnboardLedGreen,
                    bluePwmPin: device.Pins.OnboardLedBlue));

                _display = new Lazy<St7789>(_hardware.GetDisplay());

                _lightSensor = new Lazy<Bh1750>(() =>
                    new Bh1750(
                        i2cBus: I2CBus,
                        measuringMode: Bh1750.MeasuringModes.ContinuouslyHighResolutionMode, // the various modes take differing amounts of time.
                        lightTransmittance: 0.5, // lower this to increase sensitivity, for instance, if it's behind a semi opaque window
                        address: (byte)Bh1750.Addresses.Address_0x23));

                _upButton = new Lazy<PushButton>(_hardware.GetUpButton());
                _downButton = new Lazy<PushButton>(_hardware.GetDownButton());
                _leftButton = new Lazy<PushButton>(_hardware.GetLeftButton());
                _rightButton = new Lazy<PushButton>(_hardware.GetRightButton());

                _bme680 = new Lazy<Bme680>(() =>
                    new Bme680(I2CBus, (byte)Bme680.Addresses.Address_0x76));

                _speaker = new Lazy<PiezoSpeaker>(() =>
                   new PiezoSpeaker(Resolver.Device, device.Pins.D11));

                _imu = new Lazy<Bmi270>(() =>
                    new Bmi270(I2CBus));
            }
            catch (Exception ex)
            {
                Logger?.Error(ex.Message);
            }
        }

        public string HardwareRevision
        {
            get => _hardware.GetRevisionString();
        }

        public static (
            IPin MB1_CS,
            IPin MB1_INT,
            IPin MB1_PWM,
            IPin MB1_AN,
            IPin MB1_SO,
            IPin MB1_SI,
            IPin MB1_SCK,
            IPin MB1_SCL,
            IPin MB1_SDA,

            IPin MB2_CS,
            IPin MB2_INT,
            IPin MB2_PWM,
            IPin MB2_AN,
            IPin MB2_SO,
            IPin MB2_SI,
            IPin MB2_SCK,
            IPin MB2_SCL,
            IPin MB2_SDA,

            IPin A0,
            IPin D03,
            IPin D04
            ) Pins = (
            Resolver.Device.GetPin("D14"),
            Resolver.Device.GetPin("D03"),
            Resolver.Device.GetPin("D04"),
            Resolver.Device.GetPin("A00"),
            Resolver.Device.GetPin("CIPO"),
            Resolver.Device.GetPin("COPI"),
            Resolver.Device.GetPin("SCK"),
            Resolver.Device.GetPin("D08"),
            Resolver.Device.GetPin("D07"),

            Resolver.Device.GetPin("A02"),
            Resolver.Device.GetPin("D04"),
            Resolver.Device.GetPin("D03"),
            Resolver.Device.GetPin("A01"),
            Resolver.Device.GetPin("CIPO"),
            Resolver.Device.GetPin("COPI"),
            Resolver.Device.GetPin("SCK"),
            Resolver.Device.GetPin("D08"),
            Resolver.Device.GetPin("D07"),

            Resolver.Device.GetPin("A00"),
            Resolver.Device.GetPin("D03"),
            Resolver.Device.GetPin("D04")
            );
    }
}

