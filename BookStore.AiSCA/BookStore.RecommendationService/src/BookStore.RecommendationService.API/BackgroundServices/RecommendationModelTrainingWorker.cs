using BookStore.RecommendationService.Application.Services;
using BookStore.RecommendationService.Core.Abstractions;

namespace BookStore.RecommendationService.API.BackgroundServices;

/// <summary>
/// Periodically retrains the co-purchase matrix-factorization model from all captured OrderBaskets and
/// upserts the result into ProductSimilarityModel. Only registered when
/// Recommendations:ModelTrainingEnabled=true (see StartupExtensions) — a separate flag from
/// Recommendations:Enabled, which continues to gate only the Service Bus subscriber, so training can be
/// toggled independently of ingestion. Same BackgroundService shape as ReservationReleaseWorker: a
/// Task.Delay polling loop where one failed cycle logs and retries next interval rather than crashing
/// the host.
/// </summary>
public class RecommendationModelTrainingWorker : BackgroundService
{
    private readonly IOrderBasketStore _baskets;
    private readonly ICoPurchaseModelStore _modelStore;
    private readonly CoPurchaseModelTrainer _trainer;
    private readonly ILogger<RecommendationModelTrainingWorker> _logger;
    private readonly TimeSpan _interval;
    private readonly int _minBaskets;
    private readonly int _topNPerProduct;

    public RecommendationModelTrainingWorker(
        IOrderBasketStore baskets,
        ICoPurchaseModelStore modelStore,
        CoPurchaseModelTrainer trainer,
        IConfiguration configuration,
        ILogger<RecommendationModelTrainingWorker> logger)
    {
        _baskets = baskets;
        _modelStore = modelStore;
        _trainer = trainer;
        _logger = logger;

        var intervalMinutes = configuration.GetValue<int?>("Recommendations:ModelTrainingIntervalMinutes") ?? 60;
        _minBaskets = configuration.GetValue<int?>("Recommendations:MinBasketsForTraining") ?? 20;
        _topNPerProduct = configuration.GetValue<int?>("Recommendations:TrainingTopNPerProduct") ?? 20;

        if (intervalMinutes <= 0)
            throw new ArgumentOutOfRangeException("Recommendations:ModelTrainingIntervalMinutes", intervalMinutes, "must be greater than zero.");
        if (_minBaskets <= 0)
            throw new ArgumentOutOfRangeException("Recommendations:MinBasketsForTraining", _minBaskets, "must be greater than zero.");
        if (_topNPerProduct <= 0)
            throw new ArgumentOutOfRangeException("Recommendations:TrainingTopNPerProduct", _topNPerProduct, "must be greater than zero.");

        _interval = TimeSpan.FromMinutes(intervalMinutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "RecommendationModelTrainingWorker started (interval {IntervalMinutes}m, minBaskets {MinBaskets})",
            _interval.TotalMinutes, _minBaskets);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunTrainingCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recommendation model training cycle failed; will retry next interval");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunTrainingCycleAsync(CancellationToken cancellationToken)
    {
        var baskets = await _baskets.GetAllAsync(cancellationToken);
        if (baskets.Count < _minBaskets)
        {
            _logger.LogInformation(
                "Only {Count}/{Min} baskets captured — skipping training cycle", baskets.Count, _minBaskets);
            return;
        }

        var records = _trainer.Train(baskets, _topNPerProduct);
        _logger.LogInformation("Trained co-purchase model from {BasketCount} baskets — {ProductCount} products covered",
            baskets.Count, records.Count);

        foreach (var record in records)
        {
            await _modelStore.SaveAsync(record, cancellationToken);
        }
    }
}
