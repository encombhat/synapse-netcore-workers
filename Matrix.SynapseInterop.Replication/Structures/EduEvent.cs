﻿using Newtonsoft.Json.Linq;

namespace Matrix.SynapseInterop.Replication.Structures
{
    public struct EduEvent
    {
        public string content;
        public string origin;
        public string destination;
        public string edu_type;
    }
}