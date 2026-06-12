// ==============================================================================
// PATTERN DEMONSTRATION: Dependency Injection Configuration
// ==============================================================================
// This file demonstrates how all the components are wired together using
// Dependency Injection. It shows the single point where new transportation
// modes are registered in the DI container.
//
// KEY INSIGHT: This is the ONLY other place (besides RoutingStrategyPicker)
// where you need to add a line when creating a new transportation mode.
//
// REGISTRATION PATTERN:
// 1. Register PublishingSupporter (shared by all modes) → Singleton
// 2. Register each transportation mode publisher → Transient
// 3. Register RoutingStrategyPicker (factory) → Singleton
// 4. Register RouteStrategyChangedPublishService (facade) → Transient
//
// WHY THESE LIFETIMES?
// - Singleton: Shared state or expensive initialization (pickers, factories)
// - Transient: Stateless per-operation instances (publishers, facades)
// ==============================================================================

using System.Diagnostics.CodeAnalysis;
using ACME_Maps.Navigation.Messaging.Publication.ServiceBus;
using ACME_Maps.Navigation.Messaging.Publication.ServiceBus.Configuration;
using ACME_Maps.Navigation.Routing.Contract.Messaging;
using ACME_Maps.Navigation.Routing.Messaging.Configuration;
using ACME_Maps.Navigation.Routing.Messaging.RouteStrategyChangedPublish;
using ACME_Maps.Navigation.Routing.Messaging.RouteStrategyChangedPublish.Abstractions;
using ACME_Maps.Navigation.Routing.Messaging.RouteStrategyChangedPublish.Abstractions.Publishers;
using ACME_Maps.Navigation.Routing.Messaging.RouteStrategyChangedPublish.Publishers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ACME_Maps.Navigation.Routing.Messaging
{
    [ExcludeFromCodeCoverage]
    public static class ServiceRegistration
    {
        /// <summary>
        /// Configures all messaging services for route strategy change publishing.
        /// This is called during application startup to wire up the dependency injection container.
        /// </summary>
        /// <param name="services">The service collection to configure</param>
        /// <param name="configuration">Application configuration (for message bus settings)</param>
        public static IServiceCollection ConfigureMessagingServices(
            this IServiceCollection services, 
            IConfiguration configuration)
        {
            // ======================================================================
            // CONFIGURATION: Check if messaging is enabled
            // ======================================================================
            var appSettings = new ServiceBusConfig();
            configuration.Bind(appSettings);
            var shouldRegisterServiceBus = appSettings.EnableServiceBusRegistration;

            // ======================================================================
            // CORE INFRASTRUCTURE (Used by all transportation modes)
            // ======================================================================

            // Template Method helper - centralizes publishing workflow
            services.AddTransient<IPublishingSupporter, PublishingSupporter>();

            // ======================================================================
            // TRANSPORTATION MODE PUBLISHERS (One per mode)
            // ======================================================================
            // TO ADD A NEW MODE: Add one line here following the same pattern
            // Example: services.AddTransient<ITaxiRoutePublisher, TaxiRouteChangedPublisher>();
            // ======================================================================

            // Walking: Pedestrian routing with accessibility features
            services.AddTransient<IWalkingRoutePublisher, WalkingRouteChangedPublisher>();

            // Driving: Road network routing with traffic awareness
            services.AddTransient<IDrivingRoutePublisher, DrivingRouteChangedPublisher>();

            // Bicycle: Bike lane routing with elevation and safety considerations
            services.AddTransient<IBicycleRoutePublisher, BicycleRouteChangedPublisher>();

            // Public Transit: Schedule-based routing with transfers and real-time updates
            services.AddTransient<IPublicTransitRoutePublisher, PublicTransitRouteChangedPublisher>();

            // ======================================================================
            // FUTURE/PLANNED TRANSPORTATION MODES (Commented out)
            // ======================================================================
            // Uncomment these as they're implemented:
            
            // Taxi/Ride-sharing: On-demand vehicle routing
            // services.AddTransient<ITaxiRoutePublisher, TaxiRouteChangedPublisher>();

            // Scooter/Micro-mobility: Electric scooter routing
            // services.AddTransient<IScooterRoutePublisher, ScooterRouteChangedPublisher>();

            // Flight: Long-distance air travel routing
            // services.AddTransient<IFlightRoutePublisher, FlightRouteChangedPublisher>();

            // ======================================================================
            // STRATEGY FACTORY (Selects publisher based on transportation mode)
            // ======================================================================
            // Singleton: The picker caches the strategy mappings, so we want one
            // instance shared across all requests for efficiency
            services.AddSingleton<IRoutingStrategyPicker, RoutingStrategyPicker>();

            // ======================================================================
            // FACADE SERVICE (Entry point for route strategy events)
            // ======================================================================
            // Transient: Each event processing can have its own instance
            services.AddTransient<IRouteStrategyChangedPublishService, RouteStrategyChangedPublishService>();

            // ======================================================================
            // MESSAGE BUS INFRASTRUCTURE (If enabled)
            // ======================================================================
            // Only register Service Bus components if enabled in configuration
            // This allows running without messaging infrastructure during development
            if (!shouldRegisterServiceBus)
                return services;

            services.Configure<ServiceBusConfig>(configuration);
            services.AddServiceBusPublication(configuration);
            services.AddServiceBus(configuration);
            
            return services;
        }

        /// <summary>
        /// Convenience extension for WebApplicationBuilder to configure messaging services.
        /// Usage: builder.ConfigureMessagingServices();
        /// </summary>
        public static WebApplicationBuilder ConfigureMessagingServices(
            this WebApplicationBuilder builder)
        {
            builder.Services.ConfigureMessagingServices(builder.Configuration);
            return builder;
        }

        /// <summary>
        /// Registers Service Bus services for message publication.
        /// Abstracted into a separate method to keep the main configuration clean.
        /// </summary>
        private static IServiceCollection AddServiceBus(
            this IServiceCollection services, 
            IConfiguration configuration)
        {
            // Service Bus connection, topic/subscription configuration, retry policies, etc.
            // Implementation details omitted for brevity
            return services;
        }
    }
}

// ==============================================================================
// PATTERN BENEFITS DEMONSTRATED:
// ==============================================================================
// ✓ Single Responsibility: One file owns all DI registrations
//
// ✓ Open-Closed: Add new transportation modes by adding registration lines,
//   no modifications to existing registrations
//
// ✓ Explicit Dependencies: All dependencies declared upfront, making the
//   component relationships visible
//
// ✓ Testability: Can create test containers with mocked implementations
//
// ✓ Lifetime Management: Appropriate lifetimes (Singleton/Transient) for
//   each component's usage pattern
//
// ✓ Conditional Registration: Can disable messaging infrastructure during
//   development or testing
//
// ==============================================================================
// USAGE IN APPLICATION STARTUP:
// ==============================================================================
//
// PROGRAM.CS OR STARTUP.CS:
// ```csharp
// var builder = WebApplication.CreateBuilder(args);
//
// // Register all messaging services (this file)
// builder.ConfigureMessagingServices();
//
// // Register other services (API controllers, repositories, etc.)
// builder.Services.AddControllers();
// builder.Services.AddRepositories();
//
// var app = builder.Build();
// app.Run();
// ```
//
// ==============================================================================
// ADDING A NEW TRANSPORTATION MODE (Complete Checklist):
// ==============================================================================
//
// STEP 1: Create the publisher class
// ✓ Copy concrete-strategy-example.cs → TaxiRouteChangedPublisher.cs
// ✓ Update class name, interface, and message types
// ✓ Modify CreateDetailedMessage() for taxi-specific preferences
//
// STEP 2: Register in DI container (HERE!)
// ✓ Add: services.AddTransient<ITaxiRoutePublisher, TaxiRouteChangedPublisher>();
//
// STEP 3: Register in strategy factory
// ✓ In RoutingStrategyPicker.InitializeStrategies():
//   _strategies.TryAdd(nameof(NavigationProfile.TaxiRoute), 
//                      _serviceProvider.GetRequiredService<ITaxiRoutePublisher>());
//
// STEP 4: Create message classes
// ✓ TaxiRouteChangedMessage : RouteStrategyChangedBaseMessage
// ✓ TaxiRouteCreatedMessage, TaxiRouteUpdatedMessage, TaxiRouteDeletedMessage
//
// STEP 5: Create interface
// ✓ ITaxiRoutePublisher : IPublishingStrategy
//
// That's it! No changes needed to:
// - RouteStrategyChangedPublishService (Facade)
// - PublishingSupporter (Template Method)
// - Any existing publishers
//
// Total time: ~30 minutes for a complete new transportation mode!
// ==============================================================================
// REAL-WORLD ANALOGY:
// ==============================================================================
// Think of this as a restaurant kitchen setup:
//
// ConfigureMessagingServices() is like hiring the kitchen staff:
// - Hire one kitchen manager (PublishingSupporter) → used by all stations
// - Hire station chefs (WalkingRoutePublisher, DrivingRoutePublisher, etc.)
// - Hire one expeditor (RoutingStrategyPicker) → routes orders to stations
// - Hire one host (RouteStrategyChangedPublishService) → takes customer orders
//
// When you add a new menu category (transportation mode):
// - Hire a new station chef (new publisher) ← one line here
// - Tell expeditor about the new station ← one line in RoutingStrategyPicker
//
// The host (facade) and kitchen manager (template) don't need to know
// anything about the new category - they work the same for all categories!
// ==============================================================================
