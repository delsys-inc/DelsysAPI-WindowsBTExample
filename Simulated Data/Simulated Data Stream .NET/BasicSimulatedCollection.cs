using DelsysAPI.Channels.Simulated;
using DelsysAPI.Channels.Transform;
using DelsysAPI.Components.Simulated;
using DelsysAPI.Configurations;
using DelsysAPI.Configurations.Component;
using DelsysAPI.Configurations.DataSource;
using DelsysAPI.Contracts;
using DelsysAPI.DataSources;
using DelsysAPI.DelsysDevices;
using DelsysAPI.Events;
using DelsysAPI.Pipelines;
using DelsysAPI.Transforms;
using DelsysAPI.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APISimulatedDatasourceTest
{
    public class BasicSimulatedCollection
    {
        private Pipeline SimulatedPipeline;
        private ITransformManager TransformManager;

        private IDelsysDevice DeviceSource = null;

        public void InitializeDataSource()
        {
            // Load your key & license either through reflection as shown in the User Guide, or by hardcoding it to these strings.
            string key = "";
            string license = "";

            var deviceSourceCreator = new DeviceSourcePortable(key, license);
            deviceSourceCreator.SetDebugOutputStream(Console.WriteLine);
            DeviceSource = deviceSourceCreator.GetDataSource(SourceType.SIMULATED);
            DeviceSource.Key = key;
            DeviceSource.License = license;
            LoadDataSource(DeviceSource);
            Console.WriteLine("Data source loaded");
            SimulatedPipeline.Start();

            Console.ReadLine();
        }

        public void LoadDataSource(IDelsysDevice ds)
        {
            PipelineController.Instance.AddPipeline(ds);

            SimulatedPipeline = PipelineController.Instance.PipelineIds[0];
            TransformManager = PipelineController.Instance.PipelineIds[0].TransformManager;

            SimulatedPipeline.CollectionStarted += CollectionStarted;
            SimulatedPipeline.CollectionDataReady += CollectionDataReady;
            SimulatedPipeline.CollectionComplete += CollectionComplete;
            SimulatedPipeline.TrignoSimulatedManager.ComponentAdded += ComponentAdded;
            SimulatedPipeline.TrignoSimulatedManager.ComponentLost += ComponentLost;
            SimulatedPipeline.TrignoSimulatedManager.ComponentRemoved += ComponentRemoved;


            SimulatedPipeline.TrignoSimulatedManager.ComponentScanComplete += ComponentScanComplete;
            SimulatedPipeline.TrignoSimulatedManager.SimulatedTimeEventCallback += TrignoSimulatedManager_SimulatedTimeEventCallback; ;
            SimulatedPipeline.Scan();
            // Scan time in seconds
            ConfigureDataSource();
        }

        private void TrignoSimulatedManager_SimulatedTimeEventCallback(object sender, SimulatedTimeEventArg e)
        {
            Console.WriteLine("Simulated Time Event Callback @ " + e.FiredTime + "s");
            if (events.Contains(e.Sender))
            {
                Console.WriteLine("it was also simulated event in events @ " + events.Where(x => x == e.Sender).First());
                if(e.Sender == events[0])
                {
                    SimulatedPipeline.TrignoSimulatedManager.AddTimeCallback(new SimulatedTimeEvent(0.95f, 0.05f));
                }
            }
        }

        private void ComponentScanComplete(object sender, ComponentScanCompletedEventArgs e)
        {

        }

        private void ComponentRemoved(object sender, ComponentRemovedEventArgs e)
        {

        }

        private void ComponentLost(object sender, ComponentLostEventArgs e)
        {

        }

        private void ComponentAdded(object sender, ComponentAddedEventArgs e)
        {

        }

        private void CollectionComplete(object sender, CollectionCompleteEvent e)
        {
            Console.WriteLine("Received " + datasReadied + " CollectionDataReady events");
        }

        int datasReadied = 0;

        private void CollectionDataReady(object sender, ComponentDataReadyEventArgs e)
        {
            Console.WriteLine("Data collected: ");
            for (int i = 0; i < e.Data.Length; i++)
            {
                Console.WriteLine("Channel " + e.Data[i].Id);
                for (int k = 0; k < e.Data[i].Data.Count; k++)
                {
                    Console.Write(e.Data[i].Data[k] + " ");
                }
                Console.WriteLine();
            }
            
            Console.WriteLine("received a data frame (" + datasReadied + ")");
            datasReadied++;
        }

        List<SimulatedTimeEvent> events = new List<SimulatedTimeEvent>()
            {
                new SimulatedTimeEvent(0.1f),
                new SimulatedTimeEvent(0.5f),
                new SimulatedTimeEvent(0.9f)
            };

        private void CollectionStarted(object sender, CollectionStartedEvent e)
        {
            Console.WriteLine("Simulated data collection starting . . . ");
        }

        private void ConfigureDataSource()
        {
            SimDsConfig inConfig = new SimDsConfig();
            inConfig.Interval = 10;
            inConfig.SimulationTime = 5000;
            inConfig.NumberOfSensors = 1;
            inConfig.EventTimes = events;

            SensorSim sensor1 = new SensorSim(new Guid());

            List<SimCompChannel> sensor1ChannelSetup = new List<SimCompChannel>()
            {
                new SimCompChannel(10, DelsysAPI.Utils.Simulated.SignalType.Constant, Units.Hz, SignalFrequency: 1000.0f, Constants: Enumerable.Range(0, 1000).Select(x => (float)x).ToList()),
                //new SimCompChannel(10, DelsysAPI.Utils.Simulated.SignalType.Sine, Units.Orientation, SignalFrequency: 1.0f, Amplitude: 200.0f),
                //new SimCompChannel(10, DelsysAPI.Utils.Simulated.SignalType.Square, Units.Orientation, SignalFrequency: 10.0f)
            };

            SimCompConfig sensor1Channels = new SimCompConfig(sensor1ChannelSetup);
            sensor1.Configuration = sensor1Channels;

            SimulatedPipeline.TrignoSimulatedManager.AddComponent(sensor1);

            SimulatedPipeline.TrignoSimulatedManager.SelectComponentAsync(sensor1);

            SimulatedPipeline.ApplyInputConfigurations(inConfig);
            var outConfig = GenerateTransforms();
            SimulatedPipeline.ApplyOutputConfigurations(outConfig);

            SimulatedPipeline.RunTime = 100000;
        }

        /// <summary>
        /// Generates the Raw Data transform that produces our program's output.
        /// </summary>
        /// <returns>A transform configuration to be given to the API pipeline.</returns>
        public OutputConfig GenerateTransforms()
        {
            SimulatedPipeline.TransformManager.TransformList.Clear();
            //Create the transforms for the first time.
            int sensorNum = 0;
            int channelNum = 0;
            for (int i = 0; i < SimulatedPipeline.TrignoSimulatedManager.Components.Count; i++)
            {
                if (SimulatedPipeline.TrignoSimulatedManager.Components[i].State == SelectionState.Allocated)
                {
                    var tmp = SimulatedPipeline.TrignoSimulatedManager.Components[i];
                    sensorNum++;
                    channelNum += tmp.Configuration.Channels.Count;
                }
            }

            if (TransformManager.TransformList.Count == 0)
            {
                var t = new TransformRawData(channelNum, channelNum);
                TransformManager.AddTransform(t);
            }

            //channel configuration happens each time transforms are armed.
            var t0 = TransformManager.TransformList[0];
            var outconfig = new OutputConfig();
            outconfig.NumChannels = channelNum;
            int channelIndex = 0;

            for (int i = 0; i < SimulatedPipeline.TrignoSimulatedManager.Components.Count; i++)
            {
                var component = SimulatedPipeline.TrignoSimulatedManager.Components[i];
                if (component.State == SelectionState.Allocated)
                {
                    for (int k = 0; k < component.Configuration.Channels.Count; k++)
                    {
                        var chin = component.SimChannels[k];
                        var chout = new ChannelTransform(chin.FrameInterval, chin.SamplesPerFrame, Units.VOLTS);
                        TransformManager.AddInputChannel(t0, chin);
                        TransformManager.AddOutputChannel(t0, chout);
                        outconfig.MapOutputChannel(channelIndex, chout);
                        channelIndex++;
                    }
                }
            }

            return outconfig;

        }
    }
}
