#region Copyright (c) Pixeval/Pixeval.CoreApi
// GPL v3 License
// 
// Pixeval/Pixeval.CoreApi
// Copyright (c) 2023 Pixeval.CoreApi/MakoClient.cs
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Pixeval.CoreApi.Engine;
using Pixeval.CoreApi.Global.Enum;
using Pixeval.CoreApi.Global.Exception;
using Pixeval.CoreApi.Net;
using Pixeval.CoreApi.Net.EndPoints;
using Pixeval.CoreApi.Preference;
using Pixeval.Utilities;
using Refit;

namespace Pixeval.CoreApi;

public partial class MakoClient : ICancellable
{
    /// <summary>
    /// Create a new <see cref="MakoClient" /> based on given <see cref="Configuration" />, <see cref="Session" />, and
    /// <see cref="ISessionUpdate" />
    /// </summary>
    /// <remarks>
    /// The <see cref="MakoClient" /> is not responsible for the <see cref="Session" />'s refreshment, you need to check
    /// the
    /// <see cref="P:Session.Expire" /> and call <see cref="RefreshSession(Preference.Session)" /> or
    /// <see cref="RefreshSessionAsync" />
    /// periodically
    /// </remarks>
    /// <param name="session">The <see cref="Preference.Session" /></param>
    /// <param name="configuration">The <see cref="Configuration" /></param>
    /// <param name="logger"></param>
    /// <param name="sessionUpdater">The updater of <see cref="Preference.Session" /></param>
    public MakoClient(Session session, MakoClientConfiguration configuration, ILogger<MakoClient> logger, ISessionUpdate? sessionUpdater = null)
    {
        Logger = logger;
        SessionUpdater = sessionUpdater ?? new RefreshTokenSessionUpdate();
        Session = session;
        MakoServices = BuildContainer();
        Configuration = configuration;
        IsCancelled = false;
    }

    /// <summary>
    /// Creates a <see cref="MakoClient" /> based on given <see cref="Session" /> and <see cref="ISessionUpdate" />, the
    /// configurations will stay
    /// as default
    /// </summary>
    /// <remarks>
    /// The <see cref="MakoClient" /> is not responsible for the <see cref="Session" />'s refreshment, you need to check
    /// the
    /// <see cref="P:Session.Expire" /> and call <see cref="RefreshSession(Preference.Session)" /> or
    /// <see cref="RefreshSessionAsync" />
    /// periodically
    /// </remarks>
    /// <param name="session">The <see cref="Preference.Session" /></param>
    /// <param name="logger"></param>
    /// <param name="sessionUpdater">The updater of <see cref="Preference.Session" /></param>
    public MakoClient(Session session, ILogger<MakoClient> logger, ISessionUpdate? sessionUpdater = null)
    {
        Logger = logger;
        SessionUpdater = sessionUpdater ?? new RefreshTokenSessionUpdate();
        Session = session;
        MakoServices = BuildContainer();
        Configuration = new MakoClientConfiguration();
    }

    /// <summary>
    /// Injects necessary dependencies
    /// </summary>
    /// <returns>The <see cref="IContainer" /> contains all the required dependencies</returns>
    private IContainer BuildContainer()
    {
        var builder = new ContainerBuilder();
        _ = builder.RegisterInstance(this).SingleInstance();

        _ = builder.RegisterType<PixivApiNameResolver>().SingleInstance();
        _ = builder.RegisterType<PixivImageNameResolver>().SingleInstance();
        _ = builder.RegisterType<LocalMachineNameResolver>().SingleInstance();

        _ = builder.RegisterType<PixivApiHttpMessageHandler>().SingleInstance();
        _ = builder.RegisterType<PixivImageHttpMessageHandler>().SingleInstance();

        _ = builder.Register(static c => new MakoRetryHttpClientHandler(c.Resolve<PixivApiHttpMessageHandler>()))
            .Keyed<HttpMessageHandler>(typeof(PixivApiHttpMessageHandler))
            .As<HttpMessageHandler>()
            .PropertiesAutowired(static (info, _) => info.PropertyType == typeof(MakoClient))
            .SingleInstance();
        _ = builder.Register(static c => new MakoRetryHttpClientHandler(c.Resolve<PixivImageHttpMessageHandler>()))
            .Keyed<HttpMessageHandler>(typeof(PixivImageHttpMessageHandler))
            .As<HttpMessageHandler>()
            .PropertiesAutowired(static (info, _) => info.PropertyType == typeof(MakoClient))
            .SingleInstance();
        _ = builder.Register(static c => MakoHttpClient.Create(c.ResolveKeyed<HttpMessageHandler>(typeof(PixivApiHttpMessageHandler)),
                static client => client.BaseAddress = new Uri(MakoHttpOptions.AppApiBaseUrl)))
            .Keyed<HttpClient>(MakoApiKind.AppApi)
            .As<HttpClient>()
            .SingleInstance();
        _ = builder.Register(static c => MakoHttpClient.Create(c.ResolveKeyed<HttpMessageHandler>(typeof(PixivApiHttpMessageHandler)),
                static client => client.BaseAddress = new Uri(MakoHttpOptions.WebApiBaseUrl)))
            .Keyed<HttpClient>(MakoApiKind.WebApi)
            .As<HttpClient>()
            .SingleInstance();
        _ = builder.Register(static c => MakoHttpClient.Create(c.ResolveKeyed<HttpMessageHandler>(typeof(PixivApiHttpMessageHandler)),
                static client => client.BaseAddress = new Uri(MakoHttpOptions.OAuthBaseUrl)))
            .Keyed<HttpClient>(MakoApiKind.AuthApi)
            .As<HttpClient>()
            .SingleInstance();
        _ = builder.Register(static c => MakoHttpClient.Create(c.ResolveKeyed<HttpMessageHandler>(typeof(PixivImageHttpMessageHandler)),
                static client =>
                {
                    _ = client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.pixiv.net");
                    _ = client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PixivIOSApp/5.8.7");
                }))
            .Keyed<HttpClient>(MakoApiKind.ImageApi)
            .As<HttpClient>()
            .SingleInstance();

        _ = builder.Register(static c => MakoHttpOptions.CreateHttpMessageInvoker(c.Resolve<PixivApiNameResolver>()))
            .Keyed<HttpMessageInvoker>(typeof(PixivApiNameResolver))
            .As<HttpMessageInvoker>()
            .SingleInstance();
        _ = builder.Register(static c => MakoHttpOptions.CreateHttpMessageInvoker(c.Resolve<PixivImageNameResolver>()))
            .Keyed<HttpMessageInvoker>(typeof(PixivImageNameResolver))
            .As<HttpMessageInvoker>()
            .SingleInstance();
        _ = builder.Register(static _ => MakoHttpOptions.CreateDirectHttpMessageInvoker())
            .Keyed<HttpMessageInvoker>(typeof(LocalMachineNameResolver))
            .As<HttpMessageInvoker>()
            .SingleInstance();

        _ = builder.Register(static c =>
        {
            var context = c.Resolve<IComponentContext>(); // or a System.ObjectDisposedException will be thrown because the 'c' cannot be hold
            return RestService.For<IAppApiEndPoint>(c.ResolveKeyed<HttpClient>(MakoApiKind.AppApi), new RefitSettings
            {
                ExceptionFactory = async message => !message.IsSuccessStatusCode ? await MakoNetworkException.FromHttpResponseMessageAsync(message, context.Resolve<MakoClient>().Configuration.Bypass).ConfigureAwait(false) : null
            });
        });

        _ = builder.Register(static c =>
        {
            var context = c.Resolve<IComponentContext>(); // or a System.ObjectDisposedException will be thrown because the 'c' cannot be hold
            return RestService.For<IAuthEndPoint>(c.ResolveKeyed<HttpClient>(MakoApiKind.AuthApi), new RefitSettings
            {
                ExceptionFactory = async message => !message.IsSuccessStatusCode ? await MakoNetworkException.FromHttpResponseMessageAsync(message, context.Resolve<MakoClient>().Configuration.Bypass).ConfigureAwait(false) : null
            });
        });

        _ = builder.Register(static c =>
        {
            var context = c.Resolve<IComponentContext>(); // or a System.ObjectDisposedException will be thrown because the 'c' cannot be hold
            return RestService.For<IReverseSearchApiEndPoint>("https://saucenao.com/", new RefitSettings
            {
                ExceptionFactory = async message => !message.IsSuccessStatusCode ? await MakoNetworkException.FromHttpResponseMessageAsync(message, context.Resolve<MakoClient>().Configuration.Bypass).ConfigureAwait(false) : null
            });
        });
        return builder.Build();
    }

    /// <summary>
    /// Cancels this <see cref="MakoClient" />, including all the running instances, the
    /// <see cref="Session" /> will be reset to its default value, the <see cref="MakoClient" />
    /// will unable to be used again after calling this method
    /// </summary>
    public void Cancel()
    {
        Session = new Session();
        _runningInstances.ForEach(instance => instance.EngineHandle.Cancel());
    }

    /// <summary>
    /// Ensures the current instances hasn't been cancelled
    /// </summary>
    /// <exception cref="OperationCanceledException"></exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureNotCancelled()
    {
        if (IsCancelled)
        {
            ThrowUtils.InvalidOperation($"MakoClient({Id}) has been cancelled");
        }
    }

    /// <summary>
    /// Resolves a dependency of type <typeparamref name="TResult"/>
    /// </summary>
    /// <typeparam name="TResult"></typeparam>
    /// <returns></returns>
    internal TResult Resolve<TResult>() where TResult : notnull
    {
        return MakoServices.Resolve<TResult>();
    }

    /// <summary>
    /// Resolves a key-bounded dependency of type <typeparamref name="TResult"/>
    /// </summary>
    /// <typeparam name="TResult"></typeparam>
    /// <param name="key"></param>
    /// <returns></returns>
    internal TResult ResolveKeyed<TResult>(object key) where TResult : notnull
    {
        return MakoServices.ResolveKeyed<TResult>(key);
    }

    /// <summary>
    /// Resolves a dependency of type 'type'
    /// </summary>
    /// <typeparam name="TResult"></typeparam>
    /// <param name="type"></param>
    /// <returns></returns>
    internal TResult Resolve<TResult>(Type type) where TResult : notnull
    {
        return (TResult)MakoServices.Resolve(type);
    }

    internal HttpMessageInvoker GetHttpMessageInvoker(Type key)
    {
        return ResolveKeyed<HttpMessageInvoker>(key);
    }

    /// <summary>
    /// registers an instance to the running instances list
    /// </summary>
    /// <param name="engineHandleSource"></param>
    private void RegisterInstance(IEngineHandleSource engineHandleSource)
    {
        _runningInstances.Add(engineHandleSource);
    }

    /// <summary>
    /// removes an instance from the running instances list
    /// </summary>
    /// <param name="handle"></param>
    private void CancelInstance(EngineHandle handle)
    {
        _ = _runningInstances.RemoveAll(instance => instance.EngineHandle == handle);
    }

    /// <summary>
    /// <see cref="PrivacyPolicy.Private"/> is only allowed when the uid is pointing to yourself
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="privacyPolicy"></param>
    /// <returns></returns>
    private bool CheckPrivacyPolicy(long uid, PrivacyPolicy privacyPolicy)
    {
        return !(privacyPolicy == PrivacyPolicy.Private && Session.Id != uid);
    }

    /// <summary>
    /// Gets a registered <see cref="IFetchEngine{E}" /> by its <see cref="EngineHandle" />
    /// </summary>
    /// <param name="handle">The <see cref="EngineHandle" /> of the <see cref="IFetchEngine{E}" /></param>
    /// <typeparam name="T">The type of the results of the <see cref="IFetchEngine{E}" /></typeparam>
    /// <returns>The <see cref="IFetchEngine{E}" /> instance</returns>
    public IFetchEngine<T>? GetByHandle<T>(EngineHandle handle)
    {
        return _runningInstances.FirstOrDefault(h => h.EngineHandle == handle) as IFetchEngine<T>;
    }

    /// <summary>
    /// Acquires a configured <see cref="HttpClient" /> for the network traffics
    /// </summary>
    /// <param name="makoApiKind">The kind of API that is going to be used by the request</param>
    /// <returns>The <see cref="HttpClient" /> corresponding to <paramref name="makoApiKind" /></returns>
    public HttpClient GetMakoHttpClient(MakoApiKind makoApiKind)
    {
        return ResolveKeyed<HttpClient>(makoApiKind);
    }

    /// <summary>
    /// Sets the <see cref="Session" /> to a new value
    /// </summary>
    /// <param name="newSession">The new <see cref="Preference.Session" /></param>
    public void RefreshSession(Session newSession)
    {
        Session = newSession;
    }

    /// <summary>
    /// Refresh session using the provided <see cref="ISessionUpdate" />
    /// </summary>
    public async Task RefreshSessionAsync()
    {
        Session = await SessionUpdater.RefreshAsync(this).ConfigureAwait(false);
    }
}
