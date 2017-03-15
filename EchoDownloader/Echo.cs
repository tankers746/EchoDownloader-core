using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace echoDownloader
{
    class Echo
    {
        public string unit { get; set; }
        public string unitName { get; set; }
        public string title { get; set; }
        public string url { get; set; }
        public string venue { get; set; }
        public string thumbnail { get; set; }
        public string contentDir { get; set; }
        public string description { get; set; }
        public int episode { get; set; }
        public DateTime date { get; set; }
        public long duration { get; set; }
        public bool downloaded { get; set; }
    }
}
