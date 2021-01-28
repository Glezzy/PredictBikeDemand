using ForecastingBikeDemand.Model;
using Microsoft.ML;
using Microsoft.ML.TimeSeries;
using Microsoft.ML.Transforms.TimeSeries;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;


namespace ForecastingBikeDemand
{
    class BikeForecast
    {
        private static (string connectionString, string modelPath) GetConnectionString()
        {
            string rootDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../"));
            string dvFilePath = Path.Combine(rootDir, "Data", "BikeDailyDemand.mdf");
            string modelPath = Path.Combine(rootDir, "MLModel.zip");
            var connectionString = $"Data Source=(LocalDB)\\MSSQLLocalDB;AttachDbFilename={dbFilePath};Integrated Security=True;Connect Timeout=30;";
            return (connectionString, modelPath);
        }

        public static (EvaluationOutput evaluateOutput, List<ForecastOutput> forecastOutput) GetBikeForecast(int numberDfDaysToPredict)
        {
            MLContext mlContext = new MLContext();

            // Creating the DatabaseLoader that loads records of type ModelInput
            DatabaseLoader loader = mlContext.Data.CreateDatabaseLoader<ModelInput>();

            // Define the query to load the data from the database
            string query = "SELECT RentalDate, CAST(Year as REAL) as Year, CAST(TotalRentals as REAL) as TotalRentals FROM Rentals";

            DataBaseSource dbSource = new DataBaseSource(SqlClientFactory.Instance,
                GetConnectionString().connectionString, query);

            // Load the data into an IDataView
            IDataView dataView = loader.Load(dbSource);

            // Filter the data
            IDataView firstYearData = mlContext.Data.FilterRowsByColumn(dataView, "Year", upperBound: 1);
            IDataView secondYearData = mlContext.Data.FilterRowsByColumn(dataView, "Year", lowerBound: 1);

            // Define time series analysis pipeline
            var forecastingPipeline = mlContext.Forecasting.ForecastBySsa(
                outputColumnName: "ForecastedRentals",
                inputColumnName: "TotalRentals",
                windowSize: 7,
                seriesLength: 30,
                trainSize: 365,
                horizon: numberDfDaysToPredict,
                confidenceLevel: 0.95f,
                confidenceLowerBoundColumn: "LowerBoundRentals",
                confidenceUpperBoundColumn: "UpperBoundRentals");

            // We will use the fit method to train the model and fit the data to the previously defined forecasting Pipeline
            SsaForecastingTransformer forecaster = forcastingPipeline.Fit(firstYearData);

            // Evaluate the model
            EvaluateOutput evaluateOutput = EvaluateOutput(secondYearData, forecaster, mlContext);

            // Save the model
            var forecastEngine = forecaster.CreateTimeSeriesEngine<ModelInput, ModelOutput>(mlContext);
            forecastEngine.CheckPoint(mlContext, GetConnectionString().modelPath);

            // Use the model to forecast demand
            List<ForecastOutput> forecastOutput = Forecast(secondYearData, numberDfDaysToPredict, forecastEngine, mlContext);

            return (evaluateOutput, forecastOutput);
        }
        
        private static EvaluateOutput Evaluate(IDataView testData. ITransformer model, MLContext mLContext)
        {
            // Make predictions
            IDataView predictions = model.Transform(testData);

            // Actual valies
            IEnumerable<float> actual =
                mLContext.Data.CreateEnumerable<ModelOutput>(predictions, true).Select(observed => observed.TotalRentals);

            // Predicted values
            IEnumerable<float> forecast =
                mLContext.Data.CreateEnumerable<ModelOutput>(predictions, true).Select(prediction => prediction.ForecastedRentals[0]);

            // Calculate error (actual - forecast)
            var metrics = actual.Zip(forecast, (actualValue, forecast) => actualValue - forecast);

            // Get metric averages
            var MAE = metrics.Average(error => Math.Abs(error));
            var RMSE = Math.Sqrt(metrics.Average(error => Math.Pow(error, 2)));

            // Output metrics
            var evaluateOutput = new EvaluateOutput
            {
                MeanAbsoluteError = MAE,
                RootMeanSquaredError = RMSE
            };

            return evaluateOutput;
        }

        private static List<ForecastOutput> Forecast(IDataView testData, int horizon, TimeSeriesPredictionEngine<ModelInput, ModelOutput> forecaster, MLContext mLContext)
        {
            List<ForecastOutput> forecastOutputList = new List<ForecastOutput>();

            // use the predict method to forecast rentals
            ModelOutput forecast = forecaster.Predict();

            IEnumerable<ForecastOutput> forecastOutput =
                mlContext.Data.CreateEnumerable<ModelInput>(testData, reuseRowObject: false)
                .Take(horizon)
                .Select(ModelInput rental, int index) =>
                {
                string rentalDate = rental.RentalDate.ToShortDateString();
                float actualRentals = rental.TotalRentals;
                float lowerEstimate = Math.Max(0, forecast.LowerBoundRentals[index]);
                float estimate = forecast.ForecastedRentals[index];
                float upperEstimate = forecast.UpperBoundRentals[index];
                return new ForecastOutput
                {
                    Date = rentalDate,
                    ActualRentals = actualRentals,
                    LowerEstimate = lowerEstimate,
                    Forecast = estimate,
                    UpperEstimate = upperEstimate
                };
            });

            // Output predictions          
            foreach (var prediction in forecastOutput)
            {
                forecastOutputList.Add(prediction);
            }

            return forecastOutputList;
        }

        public class ModelInput
        {
            public DateTime RentalDate { get; set; }

            public float Year { get; set; }

            public float TotalRentals { get; set; }
        }

        public class ModelOutput
        {
            public float[] ForecastedRentals { get; set; }

            public float[] LowerBoundRentals { get; set; }

            public float[] UpperBoundRentals { get; set; }
        }
    }
}
