using BlazorSimpleInjectorMediator.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.SignalR;
using static System.Formats.Asn1.AsnWriter;
using System.Reflection;
using System.Runtime.CompilerServices;
using SimpleInjector.Integration.ServiceCollection;
using SimpleInjector;
using SimpleInjector.Diagnostics;
using SimpleInjector.Lifestyles;
using Microsoft.Extensions.Configuration;
using SimpleInjector.Advanced;
using System.Xml.Linq;
using MediatR;
using MediatR.Pipeline;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using SimpleInjector.Integration.AspNetCore.Mvc;


namespace BlazorSimpleInjectorMediator
{
    public class Program
    {
        class DependencyAttributePropertySelectionBehavior : IPropertySelectionBehavior
        {
            public bool SelectProperty(Type type, PropertyInfo prop) =>
                prop.GetCustomAttributes(typeof(DependencyAttribute)).Any();
        }



        public static void Main(string[] args)
        {

            Container container = new Container();


            var builder = WebApplication.CreateBuilder(args);

            container.Options.PropertySelectionBehavior =
                new DependencyAttributePropertySelectionBehavior();


            // Add services to the container.
            builder.Services.AddRazorPages();            
            builder.Services.AddSingleton<WeatherForecastService>();

            builder.Services.AddServerSideBlazor();
            // If you plan on adding AspNetCore as well, change the
            // ServiceScopeReuseBehavior to OnePerNestedScope as follows:
            // options.AddAspNetCore(ServiceScopeReuseBehavior.OnePerNestedScope);

            IntegrateSimpleInjector(builder.Services, container);            
            container.RegisterSingleton<WeatherForecastService>();
            container.Register<IMediator>(() => new Mediator(container), Lifestyle.Scoped);
            container.Register<IScoped, TestScoped>(Lifestyle.Scoped);
            container.Register<IScopedCaller, ScopedCaller>(Lifestyle.Scoped);
            container.Register<IRequestHandler<TestScopedRequest>, TestScopedRequestHandler>(Lifestyle.Scoped);
            container.Register<NavigationManagerWrapper>(Lifestyle.Scoped);

            var assemblies = new[] { typeof(Program).Assembly };
            //var assemblies = new[] { typeof(IMediator).Assembly, GetType().Assembly };
            container.Register(typeof(IRequestHandler<,>), assemblies, Lifestyle.Scoped);

            // we have to do this because by default, generic type definitions (such as the Constrained Notification Handler) won't be registered
            var notificationHandlerTypes = container.GetTypesToRegister(typeof(INotificationHandler<>), assemblies, new TypesToRegisterOptions
            {
                IncludeGenericTypeDefinitions = true,
                IncludeComposites = false
            });
            container.Collection.Register(typeof(INotificationHandler<>), notificationHandlerTypes, Lifestyle.Scoped);

            container.Collection.Register(typeof(IPipelineBehavior<,>), Enumerable.Empty<System.Type>());
            container.Collection.Register(typeof(IRequestPreProcessor<>), Enumerable.Empty<System.Type>());
            container.Collection.Register(typeof(IRequestPostProcessor<,>), Enumerable.Empty<System.Type>());



            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }
            app.Services.UseSimpleInjector(container);
            container.Verify();

            app.UseHttpsRedirection();

            app.UseStaticFiles();

            app.UseRouting();

            app.MapBlazorHub();
            app.MapFallbackToPage("/_Host");
            app.Run();
        }


        private static void IntegrateSimpleInjector(IServiceCollection services, Container container)
        {
            container.Options.DefaultScopedLifestyle = new AsyncScopedLifestyle();
            services.AddSimpleInjector(container, options =>
            {
                options.AddAspNetCore(ServiceScopeReuseBehavior.OnePerNestedScope)
                    .AddControllerActivation()
                    .AddViewComponentActivation()
                    ;
                options.AddServerSideBlazor(new[] { typeof(Program).Assembly });

            });

            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            services.AddSingleton<IControllerActivator>(new SimpleInjectorControllerActivator(container));
            services.AddSingleton<IViewComponentActivator>(new SimpleInjectorViewComponentActivator(container));                        
            services.UseSimpleInjectorAspNetRequestScoping(container);
        }
    }


    public sealed class ScopeAccessor : IAsyncDisposable, IDisposable
    {
        public SimpleInjector.Scope Scope { get; set; }
        public ValueTask DisposeAsync() => this.Scope?.DisposeAsync() ?? default;
        public void Dispose() => this.Scope?.Dispose();
    }

    public static class BlazorExtensions
    {
        public static void AddServerSideBlazor(
            this SimpleInjectorAddOptions options, params Assembly[] assemblies)
        {
            var services = options.Services;
            // Unfortunate nasty hack. We reported this with Microsoft.
            services.AddTransient(
                typeof(Microsoft.AspNetCore.Components.Server.CircuitOptions)
                    .Assembly.GetTypes().First(
                    t => t.FullName ==
                        "Microsoft.AspNetCore.Components.Server.ComponentHub"));

            services.AddScoped(
                typeof(IHubActivator<>), typeof(SimpleInjectorBlazorHubActivator<>));
            services.AddScoped<IComponentActivator, SimpleInjectorComponentActivator>();

            RegisterBlazorComponents(options, assemblies);

            services.AddScoped<ScopeAccessor>();
            services.AddTransient<ServiceScopeApplier>();            
            //var rmtypes = services.Where(s => s.ServiceType == typeof(NavigationManager) && s.ImplementationType.Name.Contains("Remote")).ToList();
            //var rmtype = services.First(s => s.ServiceType == typeof(NavigationManager) && s.ImplementationType.Name.Contains("Remote")).ImplementationType;
            //options.Container.Register(typeof(NavigationManager), rmtype, Lifestyle.Scoped);
        }

        private static void RegisterBlazorComponents(
            SimpleInjectorAddOptions options, Assembly[] assemblies)
        {
            var container = options.Container;
            var types = container.GetTypesToRegister<IComponent>(
                assemblies,
                new TypesToRegisterOptions { IncludeGenericTypeDefinitions = true });

            foreach (Type type in types.Where(t => !t.IsGenericTypeDefinition))
            {
                var registration =
                    Lifestyle.Transient.CreateRegistration(type, container);

                registration.SuppressDiagnosticWarning(
                    DiagnosticType.DisposableTransientComponent,
                    "Blazor will dispose components.");

                container.AddRegistration(type, registration);
            }

            foreach (Type type in types.Where(t => t.IsGenericTypeDefinition))
            {
                container.Register(type, type, Lifestyle.Transient);
            }
        }
    }

    public sealed class SimpleInjectorComponentActivator : IComponentActivator
    {
        private readonly ServiceScopeApplier applier;
        private readonly Container container;

        public SimpleInjectorComponentActivator(
            ServiceScopeApplier applier, Container container)
        {
            this.applier = applier;
            this.container = container;
        }

        public IComponent CreateInstance(Type type)
        {
            this.applier.ApplyServiceScope();

            IServiceProvider provider = this.container;
            var component = provider.GetService(type) ?? Activator.CreateInstance(type);
            return (IComponent)component;
        }
    }

    public sealed class SimpleInjectorBlazorHubActivator<T>
        : IHubActivator<T> where T : Hub
    {
        private readonly ServiceScopeApplier applier;
        private readonly Container container;

        public SimpleInjectorBlazorHubActivator(
            ServiceScopeApplier applier, Container container)
        {
            this.applier = applier;
            this.container = container;
        }

        public T Create()
        {
            this.applier.ApplyServiceScope();
            return this.container.GetInstance<T>();
        }

        public void Release(T hub) { }
    }

    public sealed class ServiceScopeApplier
    {
        private static AsyncScopedLifestyle lifestyle = new AsyncScopedLifestyle();

        public readonly IServiceScope serviceScope;
        public readonly ScopeAccessor accessor;
        public readonly Container container;

        public ServiceScopeApplier(
            IServiceProvider requestServices, ScopeAccessor accessor, Container container)
        {
            this.serviceScope = (IServiceScope)requestServices;
            this.accessor = accessor;
            this.container = container;
        }

        public void ApplyServiceScope()
        {
            if (this.accessor.Scope is null)
            {
                var scope = AsyncScopedLifestyle.BeginScope(this.container);

                this.accessor.Scope = scope;

                scope.GetInstance<ServiceScopeProvider>().ServiceScope = this.serviceScope;
            }
            else
            {
                lifestyle.SetCurrentScope(this.accessor.Scope);
            }
        }
    }

    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class DependencyAttribute : Attribute { }

    public abstract class BaseComponent : ComponentBase, IHandleEvent
    {
        [Dependency] public ServiceScopeApplier Applier { get; set; }
        [Dependency] public IScoped TestScopedService { get; set; }

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();
        }

        Task IHandleEvent.HandleEventAsync(EventCallbackWorkItem callback, object arg)
        {
            this.Applier.ApplyServiceScope();

            var task = callback.InvokeAsync(arg);
            var shouldAwaitTask = task.Status != TaskStatus.RanToCompletion &&
                task.Status != TaskStatus.Canceled;

            StateHasChanged();

            return shouldAwaitTask ?
                CallStateHasChangedOnAsyncCompletion(task) :
                Task.CompletedTask;
        }

        private async Task CallStateHasChangedOnAsyncCompletion(Task task)
        {
            try
            {
                await task;
            }
            catch
            {
                if (task.IsCanceled) return;

                throw;
            }

            base.StateHasChanged();
        }
    }

    public class NavigationManagerWrapper
    {
        public NavigationManagerWrapper(NavigationManager navigationManager)
        {
            NavigationManager = navigationManager;
        }

        public NavigationManager NavigationManager { get; }
    }

}