// ==============================================================================
// PATTERN DEMONSTRATION: Strategy Factory Pattern + Registry
// ==============================================================================
// This class implements a Strategy Factory that selects the appropriate route
// publisher based on the transportation mode. It uses lazy initialization and
// caching to optimize performance while maintaining clean separation of concerns.
//
// KEY RESPONSIBILITIES:
// - Factory: Create/retrieve the correct publisher for each transportation mode
// - Registry: Maintain a cached dictionary of transportation mode → publisher mappings
// - Lazy Initialization: Build the strategy registry only when first needed
// - Null Object Support: Return null for unsupported transportation modes gracefully
//
// OPEN-CLOSED PRINCIPLE: To add a new transportation mode (e.g., taxi, scooter):
// 1. Create the new publisher class (e.g., TaxiRouteChangedPublisher)
// 2. Add ONE line to InitializeStrategies() method below
// 3. That's it! No other code changes needed.
// ==============================================================================

using ACME_Maps.Navigation.Routing.Messaging.RouteStrategyChangedPublish.Abstractions;
using ACME_Maps.Navigation.Routing.Messaging.RouteStrategyChangedPublish.Abstractions.Publishers;
using ACME_Maps.Navigation.Routing.Model.Models;
using ACME_Maps.Navigation.Routing.Model.Models.Navigation;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace ACME_Maps.Navigation.Routing.Messaging.RouteStrategyChangedPublish
{
    /// <summary>
    /// Factory that picks the appropriate route publishing strategy based on transportation mode.
    /// Uses a thread-safe cached dictionary for fast strategy lookup.
    /// </summary>
    public class RoutingStrategyPicker : IRoutingStrategyPicker
    {
        private readonly IServiceProvider _serviceProvider;
        
        // Thread-safe dictionary caching transportation mode → publisher mappings
        // Key: Transportation mode name (e.g., "WalkingRoute", "DrivingRoute")
        // Value: The corresponding publisher strategy instance
        private readonly ConcurrentDictionary<string, IPublishingStrategy> _strategies = new();

        public RoutingStrategyPicker(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Picks the appropriate publishing strategy for the given route strategy.
        /// Returns null for unsupported transportation modes (Null Object pattern).
        /// </summary>
        /// <param name="routeStrategy">The route strategy containing transportation mode</param>
        /// <returns>Publishing strategy for the mode, or null if unsupported</returns>
        public IPublishingStrategy Pick(RouteStrategy routeStrategy)
        {
            // Lazy initialization: Build the registry only when first needed
            if (_strategies.IsEmpty)
            {
                InitializeStrategies();
            }

            // Attempt to retrieve the publisher for this transportation mode
            _strategies.TryGetValue(routeStrategy?.TransportationMode, out var publisher);

            // Note: Returns null for unsupported modes (handled by Facade layer)
            // This is intentional - enables graceful degradation
            if (publisher == null)
            {
                throw new ArgumentOutOfRangeException(
                    $"Unknown transportation mode: {routeStrategy?.TransportationMode}.");
            }
            
            return publisher;
        }

        /// <summary>
        /// Gets all transportation modes that have registered publishers.
        /// Useful for UI dropdowns or determining which modes are supported.
        /// </summary>
        public ICollection<string> GetAvailableTransportationModes()
        {
            if (_strategies.IsEmpty)
            {
                InitializeStrategies();
            }
            
            return _strategies.Keys;
        }

        /// <summary>
        /// Initializes the strategy registry by mapping transportation modes to publishers.
        /// 
        /// THIS IS THE ONLY METHOD YOU NEED TO MODIFY TO ADD NEW TRANSPORTATION MODES!
        /// 
        /// To add a new mode:
        /// 1. Create publisher class: TaxiRouteChangedPublisher : ITaxiRoutePublisher
        /// 2. Register in DI container (see dependency-injection-example.cs)
        /// 3. Add ONE line here: _strategies.TryAdd(nameof(...), _serviceProvider.GetRequiredService<ITaxiRoutePublisher>());
        /// </summary>
        private void InitializeStrategies()
        {
            // ========================================================================
            // CORE TRANSPORTATION MODES
            // ========================================================================
            
            // Walking: Pedestrian routing with accessibility features
            _strategies.TryAdd(
                nameof(NavigationProfile.WalkingRoute), 
                _serviceProvider.GetRequiredService<IWalkingRoutePublisher>());

            // Driving: Road network routing with traffic awareness
            _strategies.TryAdd(
                nameof(NavigationProfile.DrivingRoute), 
                _serviceProvider.GetRequiredService<IDrivingRoutePublisher>());

            // Bicycle: Bike lane routing with elevation and safety considerations
            _strategies.TryAdd(
                nameof(NavigationProfile.BicycleRoute), 
                _serviceProvider.GetRequiredService<IBicycleRoutePublisher>());

            // Public Transit: Schedule-based routing with transfers and real-time updates
            _strategies.TryAdd(
                nameof(NavigationProfile.PublicTransitRoute), 
                _serviceProvider.GetRequiredService<IPublicTransitRoutePublisher>());

            // ========================================================================
            // NULL OBJECT PATTERN EXAMPLES
            // ========================================================================
            // Some transportation modes exist in the domain model but don't have
            // publishers yet. We explicitly register them as null to document that
            // they're intentionally not supported (rather than forgotten).
            
            // Pedestrian Accessibility: Feature planned for future release
            _strategies.TryAdd(
                nameof(NavigationProfile.PedestrianAccessibility), 
                null!);

            // Traffic Conditions: Read-only data, no publishing needed
            _strategies.TryAdd(
                nameof(NavigationProfile.TrafficConditions), 
                null!);

            // ========================================================================
            // EXTENSIBILITY EXAMPLES (Commented out - shows how easy it is to add)
            // ========================================================================
            
            // Uncomment to enable taxi/ride-sharing routing:
            // _strategies.TryAdd(
            //     nameof(NavigationProfile.TaxiRoute), 
            //     _serviceProvider.GetRequiredService<ITaxiRoutePublisher>());

            // Uncomment to enable micro-mobility routing:
            // _strategies.TryAdd(
            //     nameof(NavigationProfile.ScooterRoute), 
            //     _serviceProvider.GetRequiredService<IScooterRoutePublisher>());

            // Uncomment to enable long-distance travel routing:
            // _strategies.TryAdd(
            //     nameof(NavigationProfile.FlightRoute), 
            //     _serviceProvider.GetRequiredService<IFlightRoutePublisher>());
        }
    }
}

// ==============================================================================
// PATTERN BENEFITS DEMONSTRATED:
// ==============================================================================
// ✓ Single Point of Registration: All transportation mode mappings in one place
//
// ✓ Open-Closed Principle: Add new modes by extending InitializeStrategies(),
//   no modifications to existing code
//
// ✓ Lazy Initialization: Registry built only when first needed, saving resources
//
// ✓ Thread-Safe: ConcurrentDictionary ensures safe access from multiple threads
//
// ✓ Dependency Injection: Publishers resolved from DI container, enabling testing
//
// ✓ Null Object Pattern: Explicitly documents unsupported modes vs. accidental omission
//
// ✓ Query Support: GetAvailableTransportationModes() enables runtime discovery
//   of supported modes (useful for UI generation)
//
// ==============================================================================
// REAL-WORLD ANALOGY:
// ==============================================================================
// Think of this as a restaurant menu system:
// - Menu (RoutingStrategyPicker) lists all available dishes
// - Customer orders a dish by name (transportation mode)
// - Kitchen (Factory) prepares the appropriate dish (publisher)
// - Some menu items are "86'd" / out of stock (null publishers)
// - Adding a new dish only requires updating the menu (one line change)
// ==============================================================================
