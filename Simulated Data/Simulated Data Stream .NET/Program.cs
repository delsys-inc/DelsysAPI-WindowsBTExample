using APISimulatedDatasourceTest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Simulated_Data_Stream.NET
{
    class Program
    {
        static void Main(string[] args)
        {
            BasicSimulatedCollection simCollection = new BasicSimulatedCollection();
            simCollection.InitializeDataSource();
        }
    }
}
