﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace BingelIT.MyHome.Heatronic.HeatronicUwpApp.AppServiceClients.Heatronic.Dto
{
    [DataContract]
    public class HeizkreisSteuerungDTO : HeatronicDTO
    {
        [DataMember]
        public int Heizreis { get; internal set; }
    }
}
