using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Custom_Transform.NET
{
    public class FibonacciTransform : DelsysAPI.Transforms.Transform
    {
        public FibonacciTransform(int inputChans, int outputChans) : base(inputChans, outputChans)
        {
        }

        public override void ProcessData()
        {
            for (int i = 0; i < InputChannels.Count; i++)
            {
                for(int j = 0; j < InputChannels[i].Samples.Count; j++)
                {
                    double fibValue = InputChannels[i].Samples[j];
                    if (j - 2 > 0)
                    {
                        fibValue = OutputChannels[i].Samples[j - 2] + OutputChannels[i].Samples[j - 1];
                    }
                    OutputChannels[i].AddSample(fibValue);
                }
            }
        }

        public override bool VerifySampleRates()
        {
            for (int i = 0; i < InputChannels.Count; i++)
                //check identical sampling rates for input and output channels
                if (Math.Abs(InputChannels[i].SampleRate - OutputChannels[i].SampleRate) != 0.0)
                    return false;
            return true;
        }
    }
}
