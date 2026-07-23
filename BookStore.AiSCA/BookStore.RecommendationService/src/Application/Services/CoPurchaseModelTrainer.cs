using BookStore.RecommendationService.Core.Entities;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using Microsoft.ML.Trainers.Recommender;

namespace BookStore.RecommendationService.Application.Services;

/// <summary>
/// Trains a one-class matrix-factorization model over order baskets (row = OrderId, column =
/// ProductId, implicit positive-only feedback) and turns the learned per-product latent factors into
/// ranked "also bought" neighbor lists via cosine similarity. A pure function over domain types — no
/// ML.NET type ever leaks past this class — so it stays unit-testable the same way the rest of this
/// service is. Fixing both MLContext's seed and NumberOfThreads=1 makes training reproducible
/// (verified empirically: identical output across repeated runs at both toy and production-like
/// scale) — callers/tests may still prefer qualitative ranking assertions over exact scores, since
/// reproducibility here is a property of this specific ML.NET version, not a documented API guarantee.
/// </summary>
public class CoPurchaseModelTrainer
{
    private const int DefaultApproximationRank = 8;
    private const int DefaultNumberOfIterations = 100;

    private readonly int? _seed;

    /// <param name="seed">Optional fixed seed for MLContext, so tests can pin down reproducible
    /// output. Defaults to null (a fresh random seed per Train call), preserving production's
    /// existing per-cycle retraining behavior.</param>
    public CoPurchaseModelTrainer(int? seed = null)
    {
        _seed = seed;
    }

    public IReadOnlyList<CoPurchaseModelRecord> Train(IReadOnlyList<OrderBasket> baskets, int topNPerProduct)
    {
        var productIndex = new Dictionary<Guid, uint>();
        var productReverseIndex = new Dictionary<uint, Guid>();
        var orderIndex = new Dictionary<Guid, uint>();

        var rows = new List<BasketProductEntry>();
        foreach (var basket in baskets)
        {
            if (!orderIndex.TryGetValue(basket.OrderId, out var orderIdx))
            {
                orderIdx = (uint)orderIndex.Count;
                orderIndex[basket.OrderId] = orderIdx;
            }

            foreach (var productId in basket.ProductIds)
            {
                if (!productIndex.TryGetValue(productId, out var productIdx))
                {
                    productIdx = (uint)productIndex.Count;
                    productIndex[productId] = productIdx;
                    productReverseIndex[productIdx] = productId;
                }

                rows.Add(new BasketProductEntry { OrderIndex = orderIdx, ProductIndex = productIdx, Label = 1f });
            }
        }

        if (productIndex.Count < 2 || rows.Count == 0)
        {
            return Array.Empty<CoPurchaseModelRecord>();
        }

        var mlContext = new MLContext(seed: _seed);

        var schemaDefinition = SchemaDefinition.Create(typeof(BasketProductEntry));
        schemaDefinition[nameof(BasketProductEntry.OrderIndex)].ColumnType =
            new KeyDataViewType(typeof(uint), orderIndex.Count);
        schemaDefinition[nameof(BasketProductEntry.ProductIndex)].ColumnType =
            new KeyDataViewType(typeof(uint), productIndex.Count);

        var dataView = mlContext.Data.LoadFromEnumerable(rows, schemaDefinition);

        var options = new MatrixFactorizationTrainer.Options
        {
            MatrixRowIndexColumnName = nameof(BasketProductEntry.OrderIndex),
            MatrixColumnIndexColumnName = nameof(BasketProductEntry.ProductIndex),
            LabelColumnName = nameof(BasketProductEntry.Label),
            LossFunction = MatrixFactorizationTrainer.LossFunctionType.SquareLossOneClass,
            ApproximationRank = Math.Min(DefaultApproximationRank, productIndex.Count - 1),
            NumberOfIterations = DefaultNumberOfIterations,
            // Single-threaded: this container's CPU allocation (see Helm values.yaml) is already
            // thin, multiple threads wouldn't meaningfully speed up a dataset this size, and it
            // removes any thread-interleaving source of run-to-run nondeterminism.
            NumberOfThreads = 1,
            Quiet = true
        };

        var trainer = mlContext.Recommendation().Trainers.MatrixFactorization(options);
        var model = trainer.Fit(dataView);

        var itemFactors = ExtractProductFactors(model.Model, productIndex.Count, options.ApproximationRank);

        var trainedAtUtc = DateTime.UtcNow;
        var modelVersion = trainedAtUtc.ToString("O");
        var records = new List<CoPurchaseModelRecord>();

        foreach (var (productId, idx) in productIndex)
        {
            var neighbors = new List<CoPurchaseModelNeighbor>();
            for (var otherIdx = 0; otherIdx < productIndex.Count; otherIdx++)
            {
                if (otherIdx == idx) continue;

                var score = CosineSimilarity(itemFactors[(int)idx], itemFactors[otherIdx]);
                neighbors.Add(new CoPurchaseModelNeighbor { ProductId = productReverseIndex[(uint)otherIdx], Score = score });
            }

            records.Add(new CoPurchaseModelRecord
            {
                Id = productId.ToString(),
                ProductId = productId,
                ModelVersion = modelVersion,
                TrainedAtUtc = trainedAtUtc,
                Neighbors = neighbors.OrderByDescending(n => n.Score).Take(topNPerProduct).ToList()
            });
        }

        return records;
    }

    private static float[][] ExtractProductFactors(MatrixFactorizationModelParameters model, int productCount, int rank)
    {
        // MatrixColumnIndexColumnName was bound to ProductIndex above, so the model's "right"/column
        // factor matrix holds one latent vector per product — RightFactorMatrix is row-major flattened.
        var flattened = model.RightFactorMatrix.ToArray();
        var factors = new float[productCount][];
        for (var i = 0; i < productCount; i++)
        {
            factors[i] = new float[rank];
            Array.Copy(flattened, i * rank, factors[i], 0, rank);
        }

        return factors;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0) return 0;
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private class BasketProductEntry
    {
        public uint OrderIndex;
        public uint ProductIndex;
        public float Label;
    }
}
