// ==============================================================================
// PATTERN DEMONSTRATION: Template Method Pattern (Functional Style)
// ==============================================================================
// This class implements the Template Method pattern using functional delegates
// instead of inheritance. It defines the invariant publishing workflow while
// allowing specific publishers to inject custom message transformation logic.
//
// TEMPLATE METHOD STEPS (Fixed):
// 1. Create base message with common metadata ✓
// 2. Apply transportation-mode-specific transformations (via delegate) ✓
// 3. Publish the message to the messaging infrastructure ✓
// 4. Handle logging and errors consistently ✓
//
// KEY INSIGHT: This eliminates ~70% duplication across all publishers!
// Instead of each publisher implementing these 4 steps, they only provide
// step 2 (the unique transformation logic).
//
// EXTENSIBILITY: New transportation modes get all this infrastructure for free.
// ==============================================================================

using ACME_Maps.Navigation.Messaging.Publication;
using ACME_Maps.Navigation.Routing.Contract.Messaging;
using ACME_Maps.Navigation.Routing.Messaging.RouteStrategyChangedPublish.Abstractions;
using ACME_Maps.Navigation.Routing.Model.Models;
using AutoMapper;
using Microsoft.Extensions.Logging;

namespace ACME_Maps.Navigation.Routing.Messaging.RouteStrategyChangedPublish
{
    /// <summary>
    /// Template Method helper that encapsulates the common publishing workflow.
    /// Eliminates code duplication by centralizing message creation, publishing,
    /// and error handling logic that's identical across all transportation modes.
    /// </summary>
    public class PublishingSupporter : IPublishingSupporter
    {
        private readonly IMessagePublisher _publisher;
        private readonly IMapper _mapper;
        private readonly ILogger<PublishingSupporter> _logger;

        public PublishingSupporter(
            IMessagePublisher publisher, 
            IMapper mapper, 
            ILogger<PublishingSupporter> logger)
        {
            _publisher = publisher;
            _mapper = mapper;
            _logger = logger;
        }

        /// <summary>
        /// Template Method: Orchestrates the publishing workflow with customizable transformation.
        /// 
        /// WORKFLOW (Fixed steps):
        /// 1. Create base message with common metadata
        /// 2. Apply transportation-mode-specific details (via fillSpecificDetailsForMessageAction)
        /// 3. Publish to messaging infrastructure
        /// 4. Log success/failure
        /// 
        /// USAGE EXAMPLE:
        /// await _supporter.Publish(
        ///     CreateWalkingRouteDetails,  // ← Only unique part per transportation mode
        ///     routeStrategy, 
        ///     routeMetadata, 
        ///     cancellationToken);
        /// </summary>
        /// <typeparam name="T">The specific message type (e.g., WalkingRouteCreatedMessage)</typeparam>
        /// <param name="fillSpecificDetailsForMessageAction">
        /// Delegate that transforms the base message into a transportation-mode-specific message.
        /// This is the ONLY part that varies between publishers!
        /// </param>
        /// <param name="routeStrategy">The route strategy being published</param>
        /// <param name="routeMetadata">Metadata about the navigation profile</param>
        /// <param name="cancellationToken">Cancellation token for async operations</param>
        public async Task Publish<T>(
            Func<RouteStrategyChangedBaseMessage, RouteStrategy, T> fillSpecificDetailsForMessageAction,
            RouteStrategy routeStrategy, 
            RouteMetadata routeMetadata, 
            CancellationToken cancellationToken)
            where T : RouteStrategyChangedBaseMessage, new()
        {
            T publishMsg = new();

            try
            {
                // ============================================================
                // STEP 1: Create base message (Common for all modes)
                // ============================================================
                var msgWithBasicInformation = _mapper.Map<T>(
                    CreateMessageWithBasicInformation(routeStrategy, routeMetadata));

                // ============================================================
                // STEP 2: Apply mode-specific transformations (Variable part)
                // ============================================================
                // This is where WalkingRoutePublisher, DrivingRoutePublisher, etc.
                // inject their unique logic. Each publisher provides a lambda that
                // knows how to transform the base message for their specific needs.
                publishMsg = fillSpecificDetailsForMessageAction.Invoke(
                    msgWithBasicInformation, 
                    routeStrategy);

                // ============================================================
                // STEP 3: Publish message (Common for all modes)
                // ============================================================
                string sessionId = routeMetadata.NavigationProfileId;

                _logger.LogDebug($"{DateTime.Now} - Start publishing message: {publishMsg}");
                
                await _publisher.PublishAsync(publishMsg, sessionId, cancellationToken)
                    .ConfigureAwait(false);
                
                _logger.LogInformation(
                    $"{DateTime.Now} - Successfully published route strategy change " +
                    $"message with ID {sessionId}");
            }
            catch (Exception ex)
            {
                // ============================================================
                // STEP 4: Error handling (Common for all modes)
                // ============================================================
                _logger.LogError(ex, 
                    $"{DateTime.Now} - Error occurred while publishing message: {publishMsg} " +
                    $"for route strategy with NavigationProfileId: {routeMetadata.NavigationProfileId} " +
                    $"and TransportationMode: {routeStrategy.TransportationMode}.");
            }
        }

        /// <summary>
        /// Creates the base message with metadata common to all transportation modes.
        /// Every message includes: ID, timestamps, navigation profile details, etc.
        /// 
        /// This is the "invariant" part that never changes regardless of whether
        /// you're publishing walking, driving, bicycle, or public transit routes.
        /// </summary>
        /// <param name="routeStrategy">Route strategy containing transportation mode info</param>
        /// <param name="routeMetadata">Navigation profile metadata</param>
        /// <returns>Base message populated with common fields</returns>
        public RouteStrategyChangedBaseMessage CreateMessageWithBasicInformation(
            RouteStrategy routeStrategy, 
            RouteMetadata routeMetadata)
        {
            var publishMsg = new RouteStrategyChangedBaseMessage()
            {
                // Unique message identifier
                Id = Guid.NewGuid().ToString(),

                // Route strategy identification
                NavigationProfileId = routeMetadata.NavigationProfileId,
                TransportationMode = routeStrategy.TransportationMode,
                SchemaVersion = routeStrategy.SchemaVersion,

                // Navigation profile characteristics
                UserPreferenceLevel = routeMetadata?.UserPreferenceLevel.ToString(),
                RegionCode = routeMetadata?.RegionCode,
                LanguageCode = routeMetadata?.LanguageCode,
                DevicePlatform = routeMetadata?.DevicePlatform.ToString(),
                AppVersion = routeMetadata?.AppVersion,

                // Audit trail
                PublishedTimestamp = DateTime.UtcNow
            };

            return publishMsg;
        }
    }
}

// ==============================================================================
// PATTERN BENEFITS DEMONSTRATED:
// ==============================================================================
// ✓ DRY Principle: Common publishing logic written once, used by all publishers
//
// ✓ Template Method (Functional): Uses delegates instead of inheritance for
//   customization points - more flexible and testable
//
// ✓ Separation of Concerns: 
//   - Infrastructure logic (how to publish) → here
//   - Business logic (what to publish) → in concrete publishers
//
// ✓ Consistent Error Handling: All publishers get the same robust error handling
//
// ✓ Consistent Logging: Uniform log messages across all transportation modes
//
// ✓ Testability: Can mock IMessagePublisher to test publishing logic in isolation
//
// ==============================================================================
// CODE DUPLICATION ELIMINATED:
// ==============================================================================
// WITHOUT this class:
// - Each of 16 publishers would implement: message creation, publishing,
//   error handling, logging → ~50 lines × 16 = ~800 lines of duplicated code
//
// WITH this class:
// - 70 lines here + each publisher only needs ~10 lines for unique logic
// - Total: 70 + (10 × 16) = 230 lines
// - Reduction: 70% less code!
//
// More importantly: Bug fixes and improvements happen in ONE place.
// ==============================================================================
// REAL-WORLD ANALOGY:
// ==============================================================================
// Think of ordering coffee at a café:
// 
// Template Method (Fixed steps):
// 1. Take customer order ✓
// 2. Prepare drink (variable: espresso, latte, cappuccino, etc.) ←
// 3. Serve in appropriate cup ✓
// 4. Collect payment ✓
//
// The barista (PublishingSupporter) handles steps 1, 3, 4 the same for every
// drink. Only step 2 changes based on what you ordered. You don't need to
// tell them how to take orders or collect payment - that's the "template".
// ==============================================================================
