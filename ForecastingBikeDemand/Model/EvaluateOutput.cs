using System;
using System.Collections.Generic;
using System.Text;

namespace ForecastingBikeDemand.Model
{
    class EvaluateOutput
    {
        public float MeanAbsoluteError { get; set; }
        public float RootMeanSquaredError { get; set; }
    }
}
