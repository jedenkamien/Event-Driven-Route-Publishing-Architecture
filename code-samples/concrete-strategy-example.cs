// ==============================================================================
// PATTERN DEMONSTRATION: Concrete Strategy Pattern
// ==============================================================================
// This class is a concrete implementation of the Strategy pattern for walking
// route preferences. It demonstrates how each transportation mode implements
// the same interface (IPublishingStrategy) while providing mode-specific logic.
//
// KEY RESPONSIBILITIES:
// - Implement the three operations: PublishCreated, PublishUpdated, PublishDeleted
// - Transform RouteStrategy domain objects into walking-specific messages
// - Delegate common publishing workflow to PublishingSupporter (Template Method)
// - Focus ONLY on walking route transformation logic
//
// PATTERN INSIGHT: This class is 90% delegation, 10% unique logic.
// The unique logic is the CreateDetailedMessage() method that knows how
// to extract walking-specific preferences (max distance, accessibility features, etc.)
//
// EXTENSIBILITY: Copy this file, rename it (e.g., TaxiRouteChangedPublisher),
// change the message types, and you're done!
// ==============================================================================

using ACME_Maps.Navigation.Routing.Contract.Api;
using ACME_Maps.Navigation.Routing.Contract.Messaging;
using ACME_Maps.Navigation.Routing.Messaging.RouteStrategyChangedPublish.Abstractions;
using ACME_Maps.Navigation.Routing.Messaging.RouteStrategyChangedPublish.Abstractions.Publishers;
using ACME_Maps.Navigation.Routing.Model.Models;
using ACME_Maps.Navigation.Routing.Model.Models.WalkingRoutes;
using AutoMapper;

namespace ACME_Maps.Navigation.Routing.Messaging.RouteStrategyChangedPublish.Publishers
{
    /// <summary>
    /// Concrete Strategy for publishing walking route preference changes.
    /// 
    /// Walking routes include preferences like:
    /// - Maximum walking distance
    /// - Accessibility requirements (wheelchair-friendly routes)
    /// - Preference for scenic routes vs. shortest distance
    /// - Avoidance of stairs or steep hills
    /// </summary>
    public class WalkingRouteChangedPublisher : IWalkingRoutePublisher
    {
        private readonly IPublishingSupporter _supporter;
        private readonly IMapper _mapper;

        public WalkingRouteChangedPublisher(IPublishingSupporter supporter, IMapper mapper)
        {
            _supporter = supporter;
            _mapper = mapper;
        }

        // ======================================================================
        // STRATEGY INTERFACE IMPLEMENTATION
        // ======================================================================
        // All three methods follow the same pattern:
        // 1. Call PublishingSupporter.Publish() (Template Method)
        // 2. Pass the appropriate message creation function
        // 3. Let the template handle the workflow
        // ======================================================================

        /// <summary>
        /// Publishes a message when new walking route preferences are created.
        /// Example: User sets up walking preferences for the first time.
        /// </summary>
        public async Task PublishCreated(
            RouteStrategy routeStrategy, 
            RouteMetadata routeMetadata, 
            CancellationToken cancellationToken)
        {
            // Delegate to template method, injecting our walking-specific logic
            await _supporter.Publish(
                CreateDetailedMessage<WalkingRouteCreatedMessage>,  // ← Unique logic
                routeStrategy, 
                routeMetadata, 
                cancellationToken);
        }

        /// <summary>
        /// Publishes a message when walking route preferences are updated.
        /// Example: User changes max walking distance from 1km to 2km.
        /// </summary>
        public async Task PublishUpdated(
            RouteStrategy routeStrategy, 
            RouteMetadata routeMetadata, 
            CancellationToken cancellationToken)
        {
            await _supporter.Publish(
                CreateDetailedMessage<WalkingRouteUpdatedMessage>,  // ← Unique logic
                routeStrategy, 
                routeMetadata, 
                cancellationToken);
        }

        /// <summary>
        /// Publishes a message when walking route preferences are deleted.
        /// Example: User removes all walking preferences from their profile.
        /// </summary>
        public async Task PublishDeleted(
            RouteStrategy routeStrategy, 
            RouteMetadata routeMetadata, 
            CancellationToken cancellationToken)
        {
            // Deleted messages only need base info (no detailed route data)
            await _supporter.Publish(
                (baseMsg, section) => CreateDeletedMessage(baseMsg),  // ← Unique logic
                routeStrategy, 
                routeMetadata, 
                cancellationToken);
        }

        // ======================================================================
        // WALKING-SPECIFIC MESSAGE TRANSFORMATION LOGIC
        // ======================================================================
        // THIS IS THE ONLY UNIQUE PART OF THIS CLASS!
        // Everything above is delegation to the template method.
        // ======================================================================

        /// <summary>
        /// Creates a detailed walking route message with all walking-specific preferences.
        /// 
        /// This is where the walking route domain knowledge lives:
        /// - Maximum walking distance limits
        /// - Accessibility requirements
        /// - Route preference settings
        /// - Hash for change detection
        /// </summary>
        /// <typeparam name="T">Message type (Created or Updated)</typeparam>
        /// <param name="baseMessage">Base message with common metadata</param>
        /// <param name="routeStrategy">Route strategy with walking preferences</param>
        /// <returns>Fully populated walking route message</returns>
        private T CreateDetailedMessage<T>(
            RouteStrategyChangedBaseMessage baseMessage, 
            RouteStrategy routeStrategy)
            where T : WalkingRouteChangedMessage
        {
            // Set message type for routing/filtering in message consumers
            baseMessage.MessageType = typeof(T).Name;

            // Map base message to walking-specific message type
            T msgToBePublished = _mapper.Map<T>(baseMessage);
            
            // Cast to walking-specific domain model
            var walkingRouteStrategy = routeStrategy as WalkingRoutePreferences;

            // ============================================================
            // WALKING-SPECIFIC DATA EXTRACTION
            // ============================================================
            // These fields are unique to walking routes and wouldn't exist
            // in driving, bicycle, or public transit messages
            
            // Hash enables change detection without comparing all fields
            msgToBePublished.Hash = walkingRouteStrategy?.Hash!;
            
            // Walking-specific preferences (max distance, accessibility, etc.)
            msgToBePublished.WalkingPreferences = _mapper.Map<IEnumerable<WalkingPreferenceDto>>(
                walkingRouteStrategy?.Preferences);

            return msgToBePublished;
        }

        /// <summary>
        /// Creates a deletion message with only base metadata (no walking-specific data needed).
        /// When preferences are deleted, we only need to know WHAT was deleted, not the details.
        /// </summary>
        private WalkingRouteDeletedMessage CreateDeletedMessage(
            RouteStrategyChangedBaseMessage baseMessage)
        {
            baseMessage.MessageType = nameof(WalkingRouteDeletedMessage);
            return _mapper.Map<WalkingRouteDeletedMessage>(baseMessage);
        }
    }
}

// ==============================================================================
// PATTERN BENEFITS DEMONSTRATED:
// ==============================================================================
// ✓ Single Responsibility: Only handles walking route transformation logic
//
// ✓ Strategy Pattern: Implements IPublishingStrategy interface, making it
//   interchangeable with DrivingRoutePublisher, BicycleRoutePublisher, etc.
//
// ✓ DRY: Delegates all common logic to PublishingSupporter template method
//
// ✓ Minimal Code: ~60 lines including comments, most of which is delegation
//
// ✓ Testability: Easy to test in isolation by mocking IPublishingSupporter
//
// ✓ Type Safety: Generic constraints ensure correct message types
//
// ==============================================================================
// COMPARISON WITH OTHER TRANSPORTATION MODES:
// ==============================================================================
// 
// WalkingRoutePublisher extracts:
// - Maximum walking distance (e.g., 2km)
// - Accessibility needs (wheelchair-friendly)
// - Scenic route preference
//
// DrivingRoutePublisher would extract:
// - Avoid highways / prefer highways
// - Toll road preferences
// - Fuel efficiency vs. speed optimization
//
// BicycleRoutePublisher would extract:
// - Elevation gain limits
// - Bike lane preference
// - Safety priority level
//
// PublicTransitRoutePublisher would extract:
// - Maximum transfer count
// - Accessibility needs (elevator-only)
// - Cost optimization vs. speed
//
// STRUCTURE IS IDENTICAL, DATA IS DIFFERENT!
// ==============================================================================
// EXTENDING WITH A NEW TRANSPORTATION MODE:
// ==============================================================================
// To add taxi/ride-sharing routing:
//
// 1. Copy this file → TaxiRouteChangedPublisher.cs
// 2. Replace "Walking" with "Taxi" throughout
// 3. Change WalkingPreferenceDto to TaxiPreferenceDto
// 4. Update CreateDetailedMessage() to extract taxi preferences:
//    - Ride type (economy, premium, shared)
//    - Max wait time
//    - Price estimate ranges
// 5. Register in DI container and RoutingStrategyPicker
//
// Total time: ~10 minutes. No changes to existing code!
// ==============================================================================
