using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnlockServer
{
    public class MybluetoothDevice
    {
        public string Name { get; set; }    
        public string Address { get; set; }
        public short Rssi { get; set; }

        public string Type { get; set; }
    }
}
