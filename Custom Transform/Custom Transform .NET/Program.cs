using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
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

namespace Custom_Transform.NET
{
    class Program
    {
        static void Main(string[] args)
        {
            BTConsoleApp rfc = new BTConsoleApp();
            Console.ReadLine();
        }
    }
    public class BTConsoleApp
    {
        Pipeline BTPipeline;
        ITransformManager TransformManager;

        /// <summary>
        /// Data structure for recording every channel of data.
        /// </summary>
        List<List<double>> Data = new List<List<double>>();

        IDelsysDevice DeviceSource = null;

        int TotalLostPackets = 0;
        int TotalDataPoints = 0;

        #region Initialization

        public BTConsoleApp()
        {
            InitializeDataSource();
            BTPipeline.Scan().Wait();
            while (BTPipeline.TrignoBtManager.Components.Count < 1)
            {
                BTPipeline.Scan().Wait();
            }
            Task.Delay(500).Wait();
            BTPipeline.TrignoBtManager.SelectComponentAsync(BTPipeline.TrignoBtManager.Components.First()).Wait();
            ConfigurePipeline();
            BTPipeline.Start();

        }

        public void InitializeDataSource()
        {
            // Load your key & license either through reflection as shown in the User Guide, or by hardcoding it to these strings.
            string key = "";
            string license = "";
            
            var deviceSourceCreator = new DelsysAPI.NET.DeviceSourcePortable(key, license);
            deviceSourceCreator.SetDebugOutputStream(Console.WriteLine);
            DeviceSource = deviceSourceCreator.GetDataSource(SourceType.TRIGNO_BT);
            DeviceSource.Key = key;
            DeviceSource.License = license;
            LoadDataSource(DeviceSource);
        }

        public void LoadDataSource(IDelsysDevice ds)
        {
            PipelineController.Instance.AddPipeline(ds);

            BTPipeline = PipelineController.Instance.PipelineIds[0];
            TransformManager = PipelineController.Instance.PipelineIds[0].TransformManager;

            BTPipeline.CollectionStarted += CollectionStarted;
            BTPipeline.CollectionDataReady += CollectionDataReady;
            BTPipeline.CollectionComplete += CollectionComplete;

            BTPipeline.TrignoBtManager.ComponentScanComplete += ComponentScanComplete;
        }

        #endregion

        #region Componenet Callbacks -- Component Added, Scan Complete

        private void ComponentScanComplete(object sender, DelsysAPI.Events.ComponentScanCompletedEventArgs e)
        {
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
                    Console.WriteLine(channelData.Data[i]);
                    if (e.Data[j].IsLostData.Count > 0 && e.Data[j].IsLostData[i])
                    {
                        lostPackets++;
                    }
                }
            }
            TotalLostPackets += lostPackets;
            TotalDataPoints += dataPoints;
        }

        private void CollectionStarted(object sender, DelsysAPI.Events.CollectionStartedEvent e)
        {
            var comps = PipelineController.Instance.PipelineIds[0].TrignoBtManager.Components;

            // Refresh the counters for display.
            TotalDataPoints = 0;
            TotalLostPackets = 0;

            // Recreate the list of data channels for recording
            int totalChannels = 0;
            for (int i = 0; i < comps.Count; i++)
            {
                for (int j = 0; j < comps[i].BtChannels.Count; j++)
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
        }

        private void CollectionComplete(object sender, DelsysAPI.Events.CollectionCompleteEvent e)
        {
            for (int i = 0; i < Data.Count; i++)
            {
                using (StreamWriter channelOutputFile = new StreamWriter("./channel" + i + "_data.csv"))
                {
                    foreach (var pt in Data[i])
                    {
                        channelOutputFile.WriteLine(pt.ToString());
                    }
                }
            }
            BTPipeline.DisarmPipeline().Wait();
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
                somecomp.Configuration.SelectSampleMode(selectedMode);

                if (somecomp.Configuration == null)
                {
                    return false;
                }
            }

            PipelineController.Instance.PipelineIds[0].ApplyInputConfigurations(inputConfiguration);
            var transformTopology = GenerateTransforms();
            PipelineController.Instance.PipelineIds[0].ApplyOutputConfigurations(transformTopology);
            PipelineController.Instance.PipelineIds[0].RunTime = 15;
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
            TransformManager.AddTransform(rawDataTransform);
            var fib = new FibonacciTransform(channelNumber, channelNumber);
            TransformManager.AddTransform(fib);

            var t0 = TransformManager.TransformList[0];
            var fibonacciTransform = TransformManager.TransformList[1];

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
                            // now reroute the raw transform's data into the fibonacci transform
                            var fibChin = chout;
                            var fibChout = new ChannelTransform(fibChin.FrameInterval, fibChin.SamplesPerFrame,
                                Units.VOLTS);
                            TransformManager.AddInputChannel(fibonacciTransform, fibChin);
                            TransformManager.AddOutputChannel(fibonacciTransform, fibChout);
                            outconfig.MapOutputChannel(channelIndex, fibChout);
                            channelIndex++;
                        }
                    }
                }
            }
            return outconfig;
        }

        #endregion
    }
}
