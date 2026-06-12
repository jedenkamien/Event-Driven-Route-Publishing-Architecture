// ==============================================================================
// PATTERN DEMONSTRATION: Message Hierarchy (Inheritance)
// ==============================================================================
// This file demonstrates the three-level message hierarchy used across all
// transportation modes. The hierarchy enables polymorphism while maintaining
// type safety and allowing transportation-mode-specific extensions.
//
// HIERARCHY STRUCTURE:
// Level 1: RouteStrategyChangedBaseMessage (Common to ALL modes)
//          ↓
// Level 2: WalkingRouteChangedMessage (Common to walking create/update/delete)
//          ↓
// Level 3: WalkingRouteCreatedMessage, WalkingRouteUpdatedMessage, WalkingRouteDeletedMessage
//
// PATTERN BENEFITS:
// - Level 1: Infrastructure can handle all messages polymorphically
// - Level 2: Shared walking-specific data (preferences, limits, hash)
// - Level 3: Operation-specific markers for message routing
//
// SAME HIERARCHY REPEATED FOR: Driving, Bicycle, PublicTransit, etc.
// ==============================================================================

using ACME_Maps.Navigation.Messaging;
using ACME_Maps.Navigation.Routing.Contract.Api;
using MediatR;
using System.Text.Json.Serialization;

namespace ACME_Maps.Navigation.Routing.Contract.Messaging
{
    // ==========================================================================
    // LEVEL 1: BASE MESSAGE (Common to ALL transportation modes)
    // ==========================================================================
    /// <summary>
    /// Base class for all route strategy change messages across all transportation modes.
    /// Contains metadata common to walking, driving, bicycle, public transit, and any
    /// future modes we add.
    /// 
    /// This enables infrastructure code to process all messages polymorphically:
    /// - Message routers can route based on MessageType
    /// - Auditing systems can log NavigationProfileId
    /// - Monitoring can track PublishedTimestamp
    /// </summary>
    public class RouteStrategyChangedBaseMessage : IMessage, IRequest
    {
        public RouteStrategyChangedBaseMessage()
        {
            Id = Guid.NewGuid().ToString();
        }

        // ======================================================================
        // MESSAGE INFRASTRUCTURE FIELDS
        // ======================================================================

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("messageType")]
        public string MessageType { get; set; }

        [JsonPropertyName("publishedTimestamp")]
        public DateTime PublishedTimestamp { get; set; }

        // ======================================================================
        // ROUTE STRATEGY IDENTIFICATION
        // ======================================================================

        [JsonPropertyName("navigationProfileId")]
        public string NavigationProfileId { get; set; }

        [JsonPropertyName("transportationMode")]
        public string TransportationMode { get; set; }

        [JsonPropertyName("schemaVersion")]
        public string SchemaVersion { get; set; }

        // ======================================================================
        // NAVIGATION PROFILE METADATA (Nullable - not all modes use all fields)
        // ======================================================================

        /// <summary>
        /// User preference level (e.g., Fastest, Shortest, MostScenic)
        /// </summary>
        [JsonPropertyName("userPreferenceLevel")]
        public string? UserPreferenceLevel { get; set; }

        /// <summary>
        /// Geographic region code (affects available roads, transit systems)
        /// </summary>
        [JsonPropertyName("regionCode")]
        public string? RegionCode { get; set; }

        /// <summary>
        /// Language code for directions and navigation instructions
        /// </summary>
        [JsonPropertyName("languageCode")]
        public string? LanguageCode { get; set; }

        /// <summary>
        /// Device platform (iOS, Android, Web) - affects available features
        /// </summary>
        [JsonPropertyName("devicePlatform")]
        public string? DevicePlatform { get; set; }

        /// <summary>
        /// App version - for compatibility tracking
        /// </summary>
        [JsonPropertyName("appVersion")]
        public string? AppVersion { get; set; }
    }

    // ==========================================================================
    // LEVEL 2: WALKING ROUTE BASE MESSAGE (Common to walking create/update/delete)
    // ==========================================================================
    /// <summary>
    /// Base class for all walking route messages (created, updated, deleted).
    /// Contains fields specific to walking routes that are shared across all operations.
    /// 
    /// Walking-specific concerns:
    /// - Maximum walking distance limits
    /// - Accessibility requirements (wheelchair-friendly paths)
    /// - Elevation preferences (avoid hills)
    /// - Scenic route preferences
    /// </summary>
    public abstract class WalkingRouteChangedMessage : RouteStrategyChangedBaseMessage
    {
        /// <summary>
        /// Hash of the walking preferences for quick change detection.
        /// Enables consumers to detect if preferences actually changed without
        /// comparing all fields individually.
        /// </summary>
        [JsonPropertyName("hash")]
        public string Hash { get; set; }

        /// <summary>
        /// Collection of walking-specific preferences.
        /// Only included in Created and Updated messages (not Deleted).
        /// </summary>
        [JsonPropertyName("walkingPreferences")]
        public IEnumerable<WalkingPreferenceDto>? WalkingPreferences { get; set; }
    }

    // ==========================================================================
    // LEVEL 3: OPERATION-SPECIFIC MESSAGES (Created, Updated, Deleted)
    // ==========================================================================
    
    /// <summary>
    /// Published when a user creates new walking route preferences.
    /// Example: First-time user sets up walking preferences in their profile.
    /// </summary>
    public class WalkingRouteCreatedMessage : WalkingRouteChangedMessage
    {
        // No additional fields - inherits everything from Level 2
        // The type itself serves as a marker for message routing
    }

    /// <summary>
    /// Published when a user updates existing walking route preferences.
    /// Example: User changes max walking distance from 1km to 2km.
    /// </summary>
    public class WalkingRouteUpdatedMessage : WalkingRouteChangedMessage
    {
        // No additional fields - inherits everything from Level 2
        // Consumers can differentiate Created vs Updated via MessageType
    }

    /// <summary>
    /// Published when a user deletes their walking route preferences.
    /// Example: User removes all walking preferences from their profile.
    /// Note: WalkingPreferences field will be null/empty for deleted messages.
    /// </summary>
    public class WalkingRouteDeletedMessage : WalkingRouteChangedMessage
    {
        // No additional fields - inherits everything from Level 2
        // Typically WalkingPreferences is null for deletion messages
    }

    // ==========================================================================
    // SUPPORTING DTOs
    // ==========================================================================

    /// <summary>
    /// Data Transfer Object representing a single walking preference setting.
    /// Examples:
    /// - MaxWalkingDistance: 2000 meters
    /// - AvoidStairs: true
    /// - PreferScenicRoutes: true
    /// - WheelchairAccessible: false
    /// </summary>
    public class WalkingPreferenceDto
    {
        [JsonPropertyName("preferenceName")]
        public string PreferenceName { get; set; }

        [JsonPropertyName("preferenceValue")]
        public string PreferenceValue { get; set; }

        [JsonPropertyName("unit")]
        public string? Unit { get; set; }  // e.g., "meters", "boolean", "percentage"
    }
}

// ==============================================================================
// PARALLEL HIERARCHIES FOR OTHER TRANSPORTATION MODES
// ==============================================================================
// The same 3-level pattern repeats for each transportation mode:
//
// DRIVING ROUTES:
// RouteStrategyChangedBaseMessage
//   ↓
// DrivingRouteChangedMessage (has DrivingPreferences, trafficAwareness, etc.)
//   ↓
// DrivingRouteCreatedMessage, DrivingRouteUpdatedMessage, DrivingRouteDeletedMessage
//
// BICYCLE ROUTES:
// RouteStrategyChangedBaseMessage
//   ↓
// BicycleRouteChangedMessage (has elevationLimits, bikeLanePreference, etc.)
//   ↓
// BicycleRouteCreatedMessage, BicycleRouteUpdatedMessage, BicycleRouteDeletedMessage
//
// PUBLIC TRANSIT ROUTES:
// RouteStrategyChangedBaseMessage
//   ↓
// PublicTransitRouteChangedMessage (has maxTransfers, accessibilityNeeds, etc.)
//   ↓
// PublicTransitRouteCreatedMessage, PublicTransitRouteUpdatedMessage, PublicTransitRouteDeletedMessage
//
// ==============================================================================
// PATTERN BENEFITS:
// ==============================================================================
// ✓ Polymorphism: Infrastructure can handle RouteStrategyChangedBaseMessage
//   regardless of specific transportation mode
//
// ✓ Type Safety: Consumers can subscribe to specific message types
//   (e.g., only WalkingRouteUpdatedMessage)
//
// ✓ DRY: Common fields (ID, timestamp, metadata) defined once at Level 1
//
// ✓ Extensibility: Add new transportation modes by creating new Level 2 + 3 classes
//
// ✓ Semantic Clarity: Message type conveys both transportation mode AND operation
//
// ✓ Serialization-Friendly: JSON property names enable clean API contracts
//
// ==============================================================================
// REAL-WORLD USAGE EXAMPLE:
// ==============================================================================
// 
// MESSAGE ROUTING:
// switch (message.MessageType)
// {
//     case nameof(WalkingRouteCreatedMessage):
//         await HandleWalkingRouteCreated(message as WalkingRouteCreatedMessage);
//         break;
//     case nameof(DrivingRouteUpdatedMessage):
//         await HandleDrivingRouteUpdated(message as DrivingRouteUpdatedMessage);
//         break;
//     // ... etc for all modes
// }
//
// POLYMORPHIC LOGGING:
// void LogMessage(RouteStrategyChangedBaseMessage message)
// {
//     _logger.LogInformation(
//         "Message {MessageType} for profile {ProfileId} at {Timestamp}",
//         message.MessageType,
//         message.NavigationProfileId,
//         message.PublishedTimestamp);
// }
//
// TYPE-SPECIFIC PROCESSING:
// void ProcessWalkingRouteUpdate(WalkingRouteUpdatedMessage message)
// {
//     foreach (var pref in message.WalkingPreferences)
//     {
//         if (pref.PreferenceName == "MaxWalkingDistance")
//         {
//             // Update walking distance cache
//         }
//     }
// }
//
// ==============================================================================
