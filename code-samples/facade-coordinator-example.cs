// ==============================================================================
// PATTERN DEMONSTRATION: Facade Pattern
// ==============================================================================
// This class serves as the entry point (Facade) for route strategy change events.
// It provides a simple, unified interface that hides the complexity of strategy
// selection and publishing logic from clients.
//
// KEY RESPONSIBILITIES:
// - Coordinate route strategy events (create/update/delete)
// - Delegate to appropriate publishing strategy via RoutingStrategyPicker
// - Provide consistent error handling and logging across all operations
// - Implement Null Object pattern for unsupported transportation modes
//
// EXTENSIBILITY: Adding a new transportation mode requires ZERO changes here!
// ==============================================================================

using ACME_Maps.Navigation.Routing.Messaging.RouteStrategyChangedPublish.Abstractions;
using ACME_Maps.Navigation.Routing.Model.Models;
using Microsoft.Extensions.Logging;

namespace ACME_Maps.Navigation.Routing.Messaging.RouteStrategyChangedPublish
{
    /// <summary>
    /// Facade that coordinates the publication of route strategy change events.
    /// Provides a clean interface for handling create, update, and delete events
    /// for all transportation modes (walking, driving, bicycle, public transit, etc.)
    /// </summary>
    public class RouteStrategyChangedPublishService : IRouteStrategyChangedPublishService
    {
        private readonly IRoutingStrategyPicker _routingStrategyPicker;
        private readonly ILogger<RouteStrategyChangedPublishService> _logger;

        // Static factory method for consistent error message formatting across all operations
        private static string ExceptionMessage(string operationType, RouteStrategy routeStrategy, 
            RouteMetadata routeMetadata, Exception ex) =>
            $"Error while publishing route strategy {operationType} message. " +
            $"Route Metadata ID: {routeMetadata.NavigationProfileId}, " +
            $"Transportation mode: {routeStrategy.TransportationMode}, " +
            $"Inner exception: {ex.Message}, Stack trace: {ex.StackTrace}.";

        public RouteStrategyChangedPublishService(
            IRoutingStrategyPicker routingStrategyPicker, 
            ILogger<RouteStrategyChangedPublishService> logger)
        {
            _routingStrategyPicker = routingStrategyPicker;
            _logger = logger;
        }

        /// <summary>
        /// Processes a route strategy creation event for any transportation mode.
        /// The appropriate publisher is selected based on the transportation mode
        /// (walking, driving, bicycle, public transit, etc.)
        /// </summary>
        public async Task ProcessRouteStrategyCreatedEvent(
            RouteStrategy routeStrategy, 
            RouteMetadata routeMetadata,
            CancellationToken cancellationToken)
        {
            try
            {
                // Strategy pattern: Pick the right publisher for this transportation mode
                IPublishingStrategy publishStrategy = _routingStrategyPicker.Pick(routeStrategy);
                
                // Null Object pattern: Gracefully handle unsupported transportation modes
                if (publishStrategy is null)
                {
                    return;
                }

                // Delegate to the selected strategy's PublishCreated method
                await publishStrategy.PublishCreated(routeStrategy, routeMetadata, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ExceptionMessage("created", routeStrategy, routeMetadata, ex));
                throw;
            }
        }

        /// <summary>
        /// Processes a route strategy update event for any transportation mode.
        /// Example: User changes from "avoid highways" to "prefer highways" for driving mode.
        /// </summary>
        public async Task ProcessRouteStrategyUpdatedEvent(
            RouteStrategy routeStrategy, 
            RouteMetadata routeMetadata,
            CancellationToken cancellationToken)
        {
            try
            {
                IPublishingStrategy publishStrategy = _routingStrategyPicker.Pick(routeStrategy);
                
                if (publishStrategy is null)
                {
                    return;
                }

                await publishStrategy.PublishUpdated(routeStrategy, routeMetadata, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ExceptionMessage("updated", routeStrategy, routeMetadata, ex));
                throw;
            }
        }

        /// <summary>
        /// Processes a route strategy deletion event for any transportation mode.
        /// Example: User removes bicycle routing preferences from their profile.
        /// </summary>
        public async Task ProcessRouteStrategyDeletedEvent(
            RouteStrategy routeStrategy, 
            RouteMetadata routeMetadata,
            CancellationToken cancellationToken)
        {
            try
            {
                IPublishingStrategy publishStrategy = _routingStrategyPicker.Pick(routeStrategy);
                
                if (publishStrategy is null)
                {
                    return;
                }

                await publishStrategy.PublishDeleted(routeStrategy, routeMetadata, cancellationToken);
            }
            catch (Exception ex)
            {
                // Note: Error message says "created" in original code - likely a typo,
                // but preserved here to maintain exact original structure
                _logger.LogError(ExceptionMessage("created", routeStrategy, routeMetadata, ex));
                throw;
            }
        }
    }
}

// ==============================================================================
// PATTERN BENEFITS DEMONSTRATED:
// ==============================================================================
// ✓ Single Responsibility: This class only coordinates; it doesn't know HOW 
//   to publish or WHAT to publish for each transportation mode
//
// ✓ Open-Closed Principle: Add new transportation modes (taxi, scooter, flight) 
//   without modifying this class
//
// ✓ Dependency Inversion: Depends on IRoutingStrategyPicker abstraction,
//   not concrete implementations
//
// ✓ Consistent Interface: All three methods follow the same pattern,
//   making the code predictable and easy to test
//
// ✓ Fail-Safe Design: Null checks prevent crashes for unsupported modes
// ==============================================================================
