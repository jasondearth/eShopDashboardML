﻿using Microsoft.MachineLearning.Runtime;
using Microsoft.MachineLearning.Runtime.Api.Experiment;
using Microsoft.MachineLearning.Runtime.Api.Experiment.Categorical;
using Microsoft.MachineLearning.Runtime.Api.Experiment.TweedieFastTree;
using Microsoft.MachineLearning.Runtime.Api.Experiment.ImportTextData;
using Microsoft.MachineLearning.Runtime.Api.Experiment.ModelOperations;
using Microsoft.MachineLearning.Runtime.Api.Experiment.SchemaManipulation;
using Microsoft.MachineLearning.Runtime.Data;
using Microsoft.MachineLearning.Runtime.EntryPoints;
using System;
using System.IO;
using Microsoft.MachineLearning;
using System.Threading.Tasks;

namespace eShopForecastModelsTrainer
{
    public class CountryModelHelper
    {
        private static TlcEnvironment tlcEnvironment = new TlcEnvironment(seed: 1);
        private static IPredictorModel model;

        /// <summary>
        /// Train and save model for predicting next month country unit sales
        /// </summary>
        /// <param name="dataPath">Input training file path</param>
        /// <param name="outputModelPath">Trained model path</param>
        public static void SaveModel(string dataPath, string outputModelPath = "country_month_fastTreeTweedie.zip")
        {
            if (File.Exists(outputModelPath))
            {
                File.Delete(outputModelPath);
            }

            using (var saveStream = File.OpenWrite(outputModelPath))
            {
                SaveCountryModel(dataPath, saveStream);
            }
        }

        /// <summary>
        /// Train and save model for predicting next month country unit sales
        /// </summary>
        /// <param name="dataPath">Input training file path</param>
        /// <param name="stream">Trained model path</param>
        public static void SaveCountryModel(string dataPath, Stream stream)
        {
            if (model == null)
            {
                model = CreateCountryModelUsingExperiment(dataPath);
            }

            model.Save(tlcEnvironment, stream);
        }

        /// <summary>
        /// Build model for predicting next month country unit sales using Experiment API
        /// </summary>
        /// <param name="dataPath">Input training file path</param>
        /// <returns></returns>
        private static IPredictorModel CreateCountryModelUsingExperiment(string dataPath)
        {
            Console.WriteLine("**********************************");
            Console.WriteLine("Training country forecasting model");


            // TlcEnvironment holds the experiment's session
            TlcEnvironment tlcEnvironment = new TlcEnvironment(seed: 1);
            Experiment experiment = tlcEnvironment.CreateExperiment();

            // First node in the workflow will be reading the source csv file, following the schema defined by dataSchema

            // This schema specifies the column name, type (TX for text or R4 for float) and column order
            // of the input training file
            // next,country,year,month,max,min,idx,count,sales,avg,prev
            var dataSchema = "col=Label:R4:0 col=country:TX:1 col=year:R4:2 col=month:R4:3 " +
                             "col=max:R4:4 col=min:R4:5 col=idx:R4:6 col=count:R4:7 " +
                             "col=sales:R4:8 col=avg:R4:9 col=prev:R4:10 " +
                             "header+ sep=,";

            var importData = new ImportText { CustomSchema = dataSchema };
            var imported = experiment.Add(importData);

            // The experiment combines columns by data types
            // First group will be made by numerical features in a vector named NumericalFeatures
            var numericalConcatenate = new ConcatColumns { Data = imported.Data };
            numericalConcatenate.AddColumn("NumericalFeatures",
                nameof(CountryData.year),
                nameof(CountryData.month),
                nameof(CountryData.max),
                nameof(CountryData.min),
                nameof(CountryData.idx),
                nameof(CountryData.count),
                nameof(CountryData.sales),
                nameof(CountryData.avg),
                nameof(CountryData.prev));
            var numericalConcatenated = experiment.Add(numericalConcatenate);

            // The second group is for categorical features, in a vecor named CategoryFeatures
            var categoryConcatenate = new ConcatColumns { Data = numericalConcatenated.OutputData };
            categoryConcatenate.AddColumn("CategoryFeatures", nameof(CountryData.country));
            var categoryConcatenated = experiment.Add(categoryConcatenate);


            var categorize = new CatTransformDict { Data = categoryConcatenated.OutputData };
            categorize.AddColumn("CategoryFeatures");
            var categorized = experiment.Add(categorize);

            // After combining columns by data type, the experiment needs all columns 
            // to be aggregated in a single column, named Features 
            var featuresConcatenate = new ConcatColumns { Data = categorized.OutputData };
            featuresConcatenate.AddColumn("Features", "NumericalFeatures", "CategoryFeatures");
            var featuresConcatenated = experiment.Add(featuresConcatenate);

            // Add the Learner to the workflow. The Learner is the machine learning algorithm used to train a model
            // In this case, we use the TweedieFastTree.TrainRegression
            var learner = new TrainRegression { TrainingData = featuresConcatenated.OutputData, NumThreads = 1 };
            var learnerOutput = experiment.Add(learner);

            // Add the Learner to the workflow. The Learner is the machine learning algorithm used to train a model
            // In this case, TweedieFastTree.TrainRegression was one of the best performing algorithms, but you can 
            // choose any other regression algorithm (StochasticDualCoordinateAscentRegressor,PoissonRegressor,...)
            var combineModels = new CombineModels
            {
                // Transformation nodes built before
                TransformModels = new ArrayVar<ITransformModel>(numericalConcatenated.Model, categoryConcatenated.Model, categorized.Model, featuresConcatenated.Model),
                // Learner
                PredictorModel = learnerOutput.PredictorModel
            };

            // Finally, add the combined model to the experiment
            var combinedModels = experiment.Add(combineModels);

            // Compile, set parameters (input files,...) and executes the experiment
            experiment.Compile();
            experiment.SetInput(importData.InputFile, new SimpleFileHandle(tlcEnvironment, dataPath, false, false));
            experiment.Run();

            // IPredictorModel is extracted from the workflow
            return experiment.GetOutput(combinedModels.PredictorModel);
        }

        /// <summary>
        /// Predict samples using saved model
        /// </summary>
        /// <param name="outputModelPath">Model file path</param>
        /// <returns></returns>
        public static async Task PredictSamples(string outputModelPath = "country_month_fastTreeTweedie.zip")
        {
            Console.WriteLine("*********************************");
            Console.WriteLine("Testing country forecasting model");

            // Read the model that has been previously saved by the method SaveModel
            var model = await PredictionModel.ReadAsync<CountryData, CountrySalesPrediction>(outputModelPath);

            // Build sample data
            var dataSample = new CountryData()
            {
                country = "17", // Netherlands
                month = 9,
                year = 2017,
                avg = 286.095F,
                max = 487.2F,
                min = 121.35F,
                idx = 33,
                prev = 8053.95F,
                count = 30,
                sales = 8582.85F
            };
            // Predict sample data
            var prediction = model.Predict(dataSample);
            Console.WriteLine($"Country: Netherlands, month: {dataSample.month + 1}, year: {dataSample.year} - Real value (US$): 5202.9, Forecasting (US$): {prediction.Score}");

            dataSample = new CountryData()
            {
                country = "17", // Netherlands
                month = 10,
                year = 2017,
                avg = 216.7875F,
                max = 384.15F,
                min = 103.35F,
                idx = 34,
                prev = 8582.85F,
                count = 24,
                sales = 5202.9F,
            };
            prediction = model.Predict(dataSample);
            Console.WriteLine($"Country: Netherlands, month: {dataSample.month + 1}, year: {dataSample.year} - Forecasting (US$):  {prediction.Score}");

            dataSample = new CountryData()
            {
                country = "33", // United States
                month = 9,
                year = 2017,
                avg = 1405.153043F,
                max = 1935.91F,
                min = 821.94F,
                idx = 33,
                prev = 42396.42F,
                count = 23,
                sales = 32318.52F
            };
            prediction = model.Predict(dataSample);
            Console.WriteLine($"Country: United States, month: {dataSample.month + 1}, year: {dataSample.year} - Real value (US$): 21373.14, Forecasting (US$): {prediction.Score}");

            dataSample = new CountryData()
            {
                country = "33", // United States
                month = 10,
                year = 2017,
                avg = 1335.82125F,
                max = 1925.01F,
                min = 780.36F,
                idx = 34,
                prev = 32318.52F,
                count = 16,
                sales = 21373.14F,
            };
            prediction = model.Predict(dataSample);
            Console.WriteLine($"Country: United States, month: {dataSample.month + 1}, year: {dataSample.year} - Forecasting (US$):  {prediction.Score}");
        }
    }
}
