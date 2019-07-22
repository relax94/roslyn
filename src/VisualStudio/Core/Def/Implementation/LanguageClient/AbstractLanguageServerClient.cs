﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Threading;
using Microsoft.CodeAnalysis.Remote;
using Microsoft.ServiceHub.Client;
using Microsoft.VisualStudio.LanguageServices.Remote;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.LanguageService
{
    internal abstract class AbstractLanguageServerClient : ILanguageClient
    {
        private readonly Workspace _workspace;
        private readonly string _clientName;
        private readonly string _languageServerName;

        /// <summary>
        /// Gets the name of the language client (displayed to the user).
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Gets the configuration section names for the language client. This may be null if the language client
        /// does not provide settings.
        /// </summary>
        public virtual IEnumerable<string> ConfigurationSections { get; } = null;

        /// <summary>
        /// Gets the initialization options object the client wants to send when 'initialize' message is sent.
        /// This may be null if the client does not need custom initialization options.
        /// </summary>
        public virtual object InitializationOptions { get; } = null;

        /// <summary>
        /// Gets the list of file names to watch for changes.  Changes will be sent to the server via 'workspace/didChangeWatchedFiles'
        /// message.  The files to watch must be under the current active workspace.  The file names can be specified as a relative
        /// paths to the exact file, or as glob patterns following the standard in .gitignore see https://www.kernel.org/pub/software/scm/git/docs/gitignore.html files.
        /// </summary>
        public virtual IEnumerable<string> FilesToWatch { get; } = null;

#pragma warning disable CS0067 // event never used - implementing interface ILanguageClient
        public event AsyncEventHandler<EventArgs> StartAsync;
        public event AsyncEventHandler<EventArgs> StopAsync;
#pragma warning restore CS0067 // event never used

        public AbstractLanguageServerClient(Workspace workspace, string languageServerName, string clientName)
        {
            _workspace = workspace;
            _clientName = clientName;
            _languageServerName = languageServerName;
        }

        public async Task<Connection> ActivateAsync(CancellationToken cancellationToken)
        {
            var client = await _workspace.TryGetRemoteHostClientAsync(cancellationToken).ConfigureAwait(false);
            if (client == null)
            {
                // there is no OOP. either user turned it off, or process got killed.
            }

            var hostGroup = new HostGroup(client.ClientId);
            var hubClient = new HubClient(_clientName);

            var stream = await ServiceHubRemoteHostClient.Connections.RequestServiceAsync(
                _workspace,
                hubClient,
                _languageServerName,
                hostGroup,
                TimeSpan.FromMinutes(60),
                cancellationToken).ConfigureAwait(false);

            return new Connection(stream, stream);
        }

        /// <summary>
        /// Signals that the extension has been loaded.  The server can be started immediately, or wait for user action to start.  
        /// To start the server, invoke the <see cref="StartAsync"/> event;
        /// </summary>
        public async Task OnLoadedAsync()
        {
            EnusreRemoteHost();

            // wait until remote host is available before let platform know that they can activate our LSP
            var client = await _workspace.TryGetRemoteHostClientAsync(CancellationToken.None).ConfigureAwait(false);
            if (client == null)
            {
                // there is no OOP. either user turned it off, or process got killed.
                // don't ask platform to start LSP
                return;
            }

            // let platform know that they can start us
            await StartAsync.InvokeAsync(this, EventArgs.Empty).ConfigureAwait(false);

            void EnusreRemoteHost()
            {
                // this might get called before solution is fully loaded and before file is opened. 
                // we delay our OOP start until then, but user might do vsstart before that. so we make sure we start OOP if 
                // it is not running yet. multiple start is no-op
                ((RemoteHostClientServiceFactory.RemoteHostClientService)_workspace.Services.GetService<IRemoteHostClientService>()).Enable();
            }
        }

        /// <summary>
        /// Signals the extension that the language server has been successfully initialized.
        /// </summary>
        public Task OnServerInitializedAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Signals the extension that the language server failed to initialize.
        /// </summary>
        public Task OnServerInitializeFailedAsync(Exception e)
        {
            return Task.CompletedTask;
        }
    }
}
