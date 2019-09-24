using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using DelsysAPI.Channels.Transform;
using DelsysAPI.Configurations;
using DelsysAPI.Configurations.Component;
using DelsysAPI.Configurations.DataSource;
using DelsysAPI.DelsysDevices;
using DelsysAPI.Events;
using DelsysAPI.Pipelines;
using DelsysAPI.Transforms;
using DelsysAPI.Utils;
using DelsysAPI.Utils.TrignoBt;
using System.IO;
using DelsysAPI.Contracts;
using System.Diagnostics;
using Plugin.BLE.Abstractions.Contracts;

/* * * * * 

IMPORTANT NOTE:

Please rename the Delsys API NuGet package to a ".zip" extension and extract it.
Add a reference to Plugin.BLE.NET.dll and Plugin.BLE.Abstractions.dll to expose the necessary functionality
for third-party peripherals.

* * * * */

// This is an example of using a BLE-enabled Jamar Smart Hand Dynamometer device (see link below) with our API.
// https://www.4mdmedical.com/jamar-smart-hand-dynamometer.html

// The majority of the relevant code (if you're already familiar with the Delsys BT API)
// can be found at the bottom in the "Custom Jamar Peripheral region.

// The linchpin bit of code for Delsys API compatibility is "BTPipeline.TrignoBtManager.AllowPeripheralDevice("Jamar");" in the LoadDataSource function. This allows for the third-party peripheral to be detected in a scan so long as its name contains "Jamar."

namespace SimpleWindowsNETBLE
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        Pipeline BTPipeline;
        ITransformManager TransformManager;
        /// <summary>
        /// If there are no device filters, the central will connect to every Avanti sensor
        /// it detects.
        /// </summary>
        string[] DeviceFilters = new string[]
        {
        };

        /// <summary>
        /// Data structure for recording every channel of data.
        /// </summary>
        List<List<double>> Data = new List<List<double>>();

        IAdapter bleAdapter;
        IDelsysDevice DeviceSource = null;

        int TotalLostPackets = 0;
        int TotalDataPoints = 0;

        public MainWindow()
        {
            this.InitializeComponent();
            InitializeDataSource();
            //BTPipeline.Scan();
        }

        #region Initialization

        public void InitializeDataSource()
        {
            // Load your key & license either through reflection as shown in the User Guide, or by hardcoding it to these strings.
            string key = "";
            string license = "";

            var deviceSourceCreator = new DelsysAPI.NET.DeviceSourcePortable(key, license);
            deviceSourceCreator.SetDebugOutputStream(Console.WriteLine);
            DeviceSource = deviceSourceCreator.GetDataSource(SourceType.TRIGNO_BT);
            bleAdapter = Plugin.BLE.CrossBluetoothLE.Current.Adapter;
            DeviceSource.Key = key;
            DeviceSource.License = license;
            LoadDataSource(DeviceSource);
        }

        public void LoadDataSource(IDelsysDevice ds)
        {
            PipelineController.Instance.AddPipeline(ds);

            BTPipeline = PipelineController.Instance.PipelineIds[0];
            TransformManager = PipelineController.Instance.PipelineIds[0].TransformManager;

            // Device Filters allow you to specify which sensors to connect to
            foreach (var filter in DeviceFilters)
            {
                BTPipeline.TrignoBtManager.AddDeviceIDFilter(filter);
            }
			// This is an important line of code! It lets through peripherals that have Jamar in their name.
            BTPipeline.TrignoBtManager.AllowPeripheralDevice("Jamar");
            BTPipeline.CollectionStarted += CollectionStarted;
            BTPipeline.CollectionDataReady += CollectionDataReady;
            BTPipeline.CollectionComplete += CollectionComplete;
            
            // Our custom peripheral callback
            bleAdapter.DeviceConnected += BleAdapter_DeviceConnected;
            BTPipeline.TrignoBtManager.ComponentScanComplete += ComponentScanComplete;
        }

        #endregion

        #region Button Events (Scan, Start, and Stop)

        public void clk_SelectSensors(object sender, RoutedEventArgs e)
        {
            // Select every component we found and didn't filter out.
            foreach (var component in BTPipeline.TrignoBtManager.Components)
            {
                BTPipeline.TrignoBtManager.SelectComponentAsync(component).Wait();
            }
        }

        public void clk_Start(object sender, RoutedEventArgs e)
        {
            // The pipeline must be reconfigured before it can be started again.
            ConfigurePipeline();
            BTPipeline.Start();
            btn_Start.IsEnabled = false;
            btn_SelectSensors.IsEnabled = false;
            btn_Scan.IsEnabled = false;
            btn_Stop.IsEnabled = true;
        }

        public void clk_Scan(object sender, RoutedEventArgs e)
        {
            BTPipeline.Scan();
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                tbox_SensorsConnectedGUIDs.Text = "";
            }));
            btn_Scan.IsEnabled = false;
            btn_Start.IsEnabled = false;
            btn_SelectSensors.IsEnabled = false;
        }

        public void clk_Stop(object sender, RoutedEventArgs e)
        {
            BTPipeline.Stop();
            btn_Start.IsEnabled = true;
            btn_Scan.IsEnabled = true;
            btn_SelectSensors.IsEnabled = true;
            btn_Stop.IsEnabled = false;
        }

        #endregion

        #region Componenet Callbacks -- Component Added, Scan Complete
        
        private void ComponentScanComplete(object sender, DelsysAPI.Events.ComponentScanCompletedEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                tbox_SensorsConnected.Text = e.ComponentDictionary.Count.ToString();
                for (int i = 0; i < BTPipeline.TrignoBtManager.Components.Count; i++)
                {
                    tbox_SensorsConnectedGUIDs.Text += BTPipeline.TrignoBtManager.Components[i].Properties.SerialNumber.ToString() + (i == BTPipeline.TrignoBtManager.Components.Count-1 ? "" : ", ");
                    Console.WriteLine("Added a type {0} sensor . . . ", BTPipeline.TrignoBtManager.Components[i].Properties.SensorType);
                }
            }));
            
            btn_Start.IsEnabled = BTPipeline.TrignoBtManager.Components.Count > 0;
            btn_Scan.IsEnabled = true;
            btn_SelectSensors.IsEnabled = true;
        }

        #endregion

        #region Component Callbacks -- Found, Lost, Removed

        public void ComponentAdded(object sender, ComponentAddedEventArgs e)
        {
        }

        public void ComponentLost(object sender, ComponentLostEventArgs e)
        {

        }

        public void ComponentRemoved(object sender, ComponentRemovedEventArgs e)
        {

        }
        
        #endregion

        #region Collection Callbacks -- Data Ready, Colleciton Started, and Collection Complete
        public void CollectionDataReady(object sender, ComponentDataReadyEventArgs e)
        {
            int lostPackets = 0;
            int dataPoints = 0;

            // Check each data point for if it was lost or not, and add it to the sum totals.
            for (int j = 0; j < e.Data.Count(); j++)
            {
                var channelData = e.Data[j];
                Data[j].AddRange(channelData.Data);
                dataPoints += channelData.Data.Count;
                for (int i = 0; i < channelData.Data.Count; i++)
                {
                    if (e.Data[0].IsLostData[i])
                    {
                        lostPackets++;
                    }
                }
            }
            TotalLostPackets += lostPackets;
            TotalDataPoints += dataPoints;

            // No need to await this; it may affect our total throughput.
            Application.Current.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                tbox_DroppedFrameCounter.Text = TotalLostPackets.ToString() + "/" + TotalDataPoints.ToString();
            }
            ));
        }

        private void CollectionStarted(object sender, DelsysAPI.Events.CollectionStartedEvent e)
        {
            var comps = PipelineController.Instance.PipelineIds[0].TrignoBtManager.Components;
            txt_SensorsStreaming.Text = comps.Count.ToString();

            // Refresh the counters for display.
            TotalDataPoints = 0;
            TotalLostPackets = 0;

            // Recreate the list of data channels for recording
            int totalChannels = 0;
            for(int i = 0; i < comps.Count; i++)
            {
                for(int j = 0; j < comps[i].BtChannels.Count; j++)
                {
                    if (Data.Count <= totalChannels)
                    {
                        Data.Add(new List<double>());
                    }
                    else
                    {
                        Data[totalChannels] = new List<double>();
                    }
                    totalChannels++;
                }
            }
            Task.Factory.StartNew(() => {
                Stopwatch batteryUpdateTimer = new Stopwatch();
                batteryUpdateTimer.Start();
                while (BTPipeline.CurrentState == Pipeline.ProcessState.Running)
                {
                    if (batteryUpdateTimer.ElapsedMilliseconds >= 500)
                    {
                        foreach (var comp in BTPipeline.TrignoBtManager.Components)
                        {
                            if (comp == null)
                                continue;
                            Console.WriteLine("Sensor {0}: {1}% Charge", comp.Properties.SerialNumber, BTPipeline.TrignoBtManager.QueryBatteryComponentAsync(comp).Result);
                        }
                        batteryUpdateTimer.Restart();
                    }
                }
            });
        }

        private void CollectionComplete(object sender, DelsysAPI.Events.CollectionCompleteEvent e)
        {
            for (int i = 0; i < Data.Count; i++)
            {
                using (StreamWriter channelOutputFile = new StreamWriter("./channel"+i+"_data.csv"))
                {
                    foreach (var pt in Data[i])
                    {
                        channelOutputFile.WriteLine(pt.ToString());
                    }
                }
            }
            BTPipeline.DisarmPipeline().Wait();
            btn_Start.IsEnabled = true;
        }

        #endregion

        #region Data Collection Configuration

        /// <summary>
        /// Configures the input and output of the pipeline.
        /// </summary>
        /// <returns></returns>
        private bool CallbacksAdded = false;
        private bool ConfigurePipeline()
        {
            if (CallbacksAdded)
            {
                BTPipeline.TrignoBtManager.ComponentAdded -= ComponentAdded;
                BTPipeline.TrignoBtManager.ComponentLost -= ComponentLost;
                BTPipeline.TrignoBtManager.ComponentRemoved -= ComponentRemoved;
            }
            BTPipeline.TrignoBtManager.ComponentAdded += ComponentAdded;
            BTPipeline.TrignoBtManager.ComponentLost += ComponentLost;
            BTPipeline.TrignoBtManager.ComponentRemoved += ComponentRemoved;
            CallbacksAdded = true;

            TrignoBTConfig btConfigurationObject = new TrignoBTConfig { EOS = EmgOrSimulate.EMG };

            if (PortableIoc.Instance.CanResolve<TrignoBTConfig>())
            {
                PortableIoc.Instance.Unregister<TrignoBTConfig>();
            }

            PortableIoc.Instance.Register(ioc => btConfigurationObject);

            var inputConfiguration = new BTDsConfig();
            inputConfiguration.NumberOfSensors = BTPipeline.TrignoBtManager.Components.Count;
            foreach (var somecomp in BTPipeline.TrignoBtManager.Components.Where(x => x.State == SelectionState.Allocated))
            {
                string selectedMode = "EMG+IMU,ACC:+/-2g,GYRO:+/-500dps";
                // Synchronize to the UI thread and check if the mode textbox value exists in the
                // available sample modes for the sensor.
                Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                    if (somecomp.Configuration.SampleModes.Contains(tbox_SetMode.Text))
                    {
                        selectedMode = tbox_SetMode.Text;
                    }
                }));
                somecomp.Configuration.SelectSampleMode(selectedMode);

                if (somecomp.Configuration == null)
                {
                    return false;
                }
            }

            PipelineController.Instance.PipelineIds[0].ApplyInputConfigurations(inputConfiguration);
            var transformTopology = GenerateTransforms();
            PipelineController.Instance.PipelineIds[0].ApplyOutputConfigurations(transformTopology);
            PipelineController.Instance.PipelineIds[0].RunTime = int.MaxValue;
            return true;
        }

        /// <summary>
        /// Generates the Raw Data transform that produces our program's output.
        /// </summary>
        /// <returns>A transform configuration to be given to the API pipeline.</returns>
        public OutputConfig GenerateTransforms()
        {
            // Clear the previous transforms should they exist.
            TransformManager.TransformList.Clear();
            
            int channelNumber = 0;
            // Obtain the number of channels based on our sensors and their mode.
            for (int i = 0; i < BTPipeline.TrignoBtManager.Components.Count; i++)
            {
                if (BTPipeline.TrignoBtManager.Components[i].State == SelectionState.Allocated)
                {
                    var tmp = BTPipeline.TrignoBtManager.Components[i];

                    BTCompConfig someconfig = tmp.Configuration as BTCompConfig;
                    if (someconfig.IsComponentAvailable())
                    {
                        channelNumber += BTPipeline.TrignoBtManager.Components[i].BtChannels.Count;
                    }

                }
            }

            // Create the raw data transform, with an input and output channel for every
            // channel that exists in our setup. This transform applies the scaling to the raw
            // data from the sensor.
            var rawDataTransform = new TransformRawData(channelNumber, channelNumber);
            PipelineController.Instance.PipelineIds[0].TransformManager.AddTransform(rawDataTransform);

            // The output configuration for the API to use.
            var outconfig = new OutputConfig();
            outconfig.NumChannels = channelNumber;

            int channelIndex = 0;
            for (int i = 0; i < BTPipeline.TrignoBtManager.Components.Count; i++)
            {
                if (BTPipeline.TrignoBtManager.Components[i].State == SelectionState.Allocated)
                {
                    BTCompConfig someconfig = BTPipeline.TrignoBtManager.Components[i].Configuration as BTCompConfig;
                    if (someconfig.IsComponentAvailable())
                    {
                        // For every channel in every sensor, we gather its sampling information (rate, interval, units) and create a
                        // channel transform (an abstract channel used by transforms) from it. We then add the actual component's channel
                        // as an input channel, and the channel transform as an output. 
                        // Finally, we map the channel counter and the output channel. This mapping is what determines the channel order in
                        // the CollectionDataReady callback function.
                        for (int k = 0; k < BTPipeline.TrignoBtManager.Components[i].BtChannels.Count; k++)
                        {
                            var chin = BTPipeline.TrignoBtManager.Components[i].BtChannels[k];
                            var chout = new ChannelTransform(chin.FrameInterval, chin.SamplesPerFrame, BTPipeline.TrignoBtManager.Components[i].BtChannels[k].Unit);
                            TransformManager.AddInputChannel(rawDataTransform, chin);
                            TransformManager.AddOutputChannel(rawDataTransform, chout);
                            Guid tmpKey = outconfig.MapOutputChannel(channelIndex, chout);
                            channelIndex++;
                        }
                    }
                }
            }
            return outconfig;
        }

        #endregion

        #region Custom Jamar Peripheral stuff

		// The Jamar peripheral uses the Nordic UART service. More details here:
		// https://learn.adafruit.com/introducing-adafruit-ble-bluetooth-low-energy-friend/uart-service 
        public Guid NordicUARTService = Guid.ParseExact("6E400001-B5A3-F393-E0A9-E50E24DCCA9E", "d");
        public Guid NordicUARTRXCharacteristic = Guid.ParseExact("6E400002-B5A3-F393-E0A9-E50E24DCCA9E", "d");
        public Guid NordicUARTTXCharacteristic = Guid.ParseExact("6E400003-B5A3-F393-E0A9-E50E24DCCA9E", "d");

		// We use the BLE library adapter to set up our custom peripheral.
        private void BleAdapter_DeviceConnected(object sender, Plugin.BLE.Abstractions.EventArgs.DeviceEventArgs e)
        {
            if (e.Device.Name.ToLower().Contains("jamar"))
            {
                Console.WriteLine("connected to device " + e.Device.Name);
                var HandGripDevice = e.Device;
                Task.Delay(500).Wait();
                if (HandGripDevice.GetServicesAsync().Result.Count == 0)
                {
                    bleAdapter.DisconnectDeviceAsync(HandGripDevice);
                    HandGripDevice.Dispose();
                    HandGripDevice = null;
                    return;
                }
                HandGripDevice.GetServiceAsync(NordicUARTService).Result.GetCharacteristicAsync(NordicUARTTXCharacteristic).Result.ValueUpdated += TX_Updated;
                HandGripDevice.GetServiceAsync(NordicUARTService).Result.GetCharacteristicAsync(NordicUARTTXCharacteristic).Result.StartUpdatesAsync();
            }
        }
		
		// Based on their Normative Data table that came with the Jamar device.
        float normalGrip = 54.8f;
        float sd = 10.4f;

        private void TX_Updated(object sender, Plugin.BLE.Abstractions.EventArgs.CharacteristicUpdatedEventArgs e)
        {
            string stringValue = e.Characteristic.StringValue;
            string[] split = stringValue.Split(',');
            string floatString = new string(split[1].Where(x => char.IsDigit(x) || x == '.').ToArray());
            float val = float.Parse(floatString);
            Console.WriteLine(val);
        }
        #endregion
    }
}