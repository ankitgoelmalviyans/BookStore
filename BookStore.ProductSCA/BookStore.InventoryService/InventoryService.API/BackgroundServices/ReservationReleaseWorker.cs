using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookStore.InventoryService.Application.Interfaces;
using BookStore.InventoryService.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace BookStore.InventoryService.API.BackgroundServices
{
    /// <summary>
    /// Performs the physical stock release for reservation lines flagged <c>PendingRelease</c> (by a
    /// partial-reservation failure or an OrderCancelled compensation). Each line's release
    /// (<c>Reserved → available</c>) is idempotent and gated by the line's own state, so a retry after
    /// a crash is safe. A line that can't be released within its retry budget is moved to the terminal
    /// <c>ReleaseFailed</c> state with an Error log for manual reconciliation (ADR-17 posture).
    /// </summary>
    public class ReservationReleaseWorker : BackgroundService
    {
        private readonly IReservationRepository _reservations;
        private readonly IInventoryRepository _inventory;
        private readonly ILogger<ReservationReleaseWorker> _logger;
        private readonly TimeSpan _pollingInterval;
        private readonly int _batchSize;
        private readonly int _maxRetries;

        public ReservationReleaseWorker(
            IReservationRepository reservations,
            IInventoryRepository inventory,
            IConfiguration configuration,
            ILogger<ReservationReleaseWorker> logger)
        {
            _reservations = reservations;
            _inventory = inventory;
            _logger = logger;

            var pollingSeconds = configuration.GetValue<int?>("Reservations:ReleasePollingIntervalSeconds") ?? 15;
            _batchSize = configuration.GetValue<int?>("Reservations:ReleaseBatchSize") ?? 20;
            _maxRetries = configuration.GetValue<int?>("Reservations:ReleaseMaxRetries") ?? 10;

            if (pollingSeconds <= 0)
                throw new ArgumentOutOfRangeException("Reservations:ReleasePollingIntervalSeconds", pollingSeconds, "must be greater than zero.");
            if (_batchSize <= 0)
                throw new ArgumentOutOfRangeException("Reservations:ReleaseBatchSize", _batchSize, "must be greater than zero.");
            if (_maxRetries <= 0)
                throw new ArgumentOutOfRangeException("Reservations:ReleaseMaxRetries", _maxRetries, "must be greater than zero.");

            _pollingInterval = TimeSpan.FromSeconds(pollingSeconds);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ReservationReleaseWorker started (interval {IntervalSeconds}s, batch {BatchSize})",
                _pollingInterval.TotalSeconds, _batchSize);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ReleasePendingAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Reservation release cycle failed; will retry next interval");
                }

                try
                {
                    await Task.Delay(_pollingInterval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private async Task ReleasePendingAsync(CancellationToken stoppingToken)
        {
            var pending = await _reservations.GetWithPendingReleasesAsync(_batchSize, stoppingToken);

            foreach (var reservation in pending)
            {
                if (stoppingToken.IsCancellationRequested) break;

                using (LogContext.PushProperty("CorrelationId", reservation.Outbox?.CorrelationId))
                {
                    var changed = false;
                    foreach (var line in reservation.Lines)
                    {
                        if (line.Status != ReservationLineStatus.PendingRelease) continue;

                        bool released;
                        try
                        {
                            released = _inventory.TryRelease(line.ProductId, line.Quantity);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Release of product {ProductId} for order {OrderId} threw", line.ProductId, reservation.OrderId);
                            released = false;
                        }

                        if (released)
                        {
                            line.Status = ReservationLineStatus.Released;
                            changed = true;
                        }
                        else
                        {
                            line.Attempts++;
                            if (line.Attempts >= _maxRetries)
                            {
                                line.Status = ReservationLineStatus.ReleaseFailed;
                                _logger.LogError(
                                    "Order {OrderId} product {ProductId} could not be released after {Attempts} attempts (ReleaseFailed); manual reconciliation required",
                                    reservation.OrderId, line.ProductId, line.Attempts);
                            }
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        reservation.HasPendingReleases = reservation.Lines.Any(l => l.Status == ReservationLineStatus.PendingRelease);
                        reservation.LastUpdated = DateTime.UtcNow;
                        await _reservations.UpsertAsync(reservation, stoppingToken);
                    }
                }
            }
        }
    }
}
