using System;
using System.Collections.Generic;
using System.Text;

namespace ForecastingBikeDemand.Model
{
    class ForecastOutput
    {
        public string Date {get; set }
        public float ActualRentals { get; set; }
        public float LowerEstimate { get; set; }
        public float Forecast { get; set; }
        public float UpperEstimate { get; set; }
    }
}
