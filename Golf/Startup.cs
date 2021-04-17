using System.Collections.Generic;
using System.Threading.Tasks;
using EventStore.Client;
using Eventuous;
using Eventuous.EventStoreDB;
using Eventuous.EventStoreDB.Subscriptions;
using Eventuous.Projections.MongoDB;
using Eventuous.Projections.MongoDB.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Golf.Data;
using Golf.Data.Eventuous;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using UserCredentials = EventStore.Client.UserCredentials;


namespace Golf
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRazorPages();
            services.AddServerSideBlazor();
            services.AddSingleton<WeatherForecastService>();
            services.AddSingleton(_ =>
            {
                var settings = EventStoreClientSettings.Create(
                    "esdb://admin:changeit@localhost:2113?tls=false");
                settings.DefaultCredentials = new UserCredentials("admin", "changeit");
                settings.ConnectionName = "Golf";

                var client = new EventStoreClient(settings);
                return client;
            });
            services.AddSingleton<IEventStore, EsDbEventStore>();
            services.AddSingleton(_ => DefaultEventSerializer.Instance);
            services.AddSingleton<IAggregateStore, AggregateStore>();
            services.AddSingleton<IRoundViewModel, RoundViewModel>();
            services.AddSingleton<RoundService>();

            RoundEvents.Register();

            services.AddSingleton(_ =>
                    new MongoClient(
                            "mongodb://mongoadmin:secret@localhost:27017/?authSource=admin&readPreference=primary&appname=MongoDB%20Compass&ssl=false")
                        .GetDatabase("Golf"))
                .AddSingleton<ICheckpointStore, MongoCheckpointStore>();
            services.AddSingleton(o => new StatsSubscriber(
                o.GetService<EventStoreClient>(),
                "Stats",
                o.GetService<ICheckpointStore>(),
                o.GetService<IEventSerializer>(),
                new IEventHandler[]
                    {new StatsEventHandler(o.GetService<IMongoDatabase>(), "Stats", o.GetService<ILoggerFactory>())},
                o.GetService<ILoggerFactory>()
            ));
            services.AddHostedService(o=>o.GetService<StatsSubscriber>());
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapBlazorHub();
                endpoints.MapFallbackToPage("/_Host");
            });
        }
    }

    public class StatsSubscriber : AllStreamSubscriptionService
    {
        public StatsSubscriber(EventStoreClient eventStoreClient, string subscriptionId,
            ICheckpointStore checkpointStore, IEventSerializer eventSerializer,
            IEnumerable<IEventHandler> eventHandlers, ILoggerFactory loggerFactory = null,
            IEventFilter eventFilter = null, SubscriptionGapMeasure measure = null) : base(eventStoreClient,
            subscriptionId, checkpointStore, eventSerializer, eventHandlers, loggerFactory, eventFilter, measure)
        {
        }
    }

    public class StatsEventHandler : MongoProjection<Stats>
    {
        public StatsEventHandler(
            IMongoDatabase database,
            string subscriptionGroup,
            ILoggerFactory loggerFactory)
            : base(
                database,
                subscriptionGroup, 
                loggerFactory)
        {
        }

        protected override ValueTask<Operation<Stats>> GetUpdate(object evt)
        {
            return evt switch
            {
                HoleScoreSubmitted c => UpdateOperationTask(c.RoundId, update => update.Inc(s => s.Strokes, c.Strokes)),
                _ => NoOp
            };
        }
    }

    public record Stats(string Id, int Strokes) : ProjectedDocument(Id);
}