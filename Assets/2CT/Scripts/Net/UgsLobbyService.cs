// Gated until the Unity Gaming Services multiplayer package is installed AND the UGS_LOBBY
// scripting define is added (Project Settings ▸ Player ▸ Scripting Define Symbols). Without them
// this file compiles to nothing so the rest of the project builds.
#if UGS_LOBBY
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace TwoCT.Net
{
    /// <summary>
    /// Wraps the UGS Multiplayer SDK (Sessions API) so the game can host and browse online games.
    /// A "session" bundles a public lobby entry with a Relay-networked NGO connection: creating one
    /// starts hosting through Relay and lists it publicly; joining one connects the client through
    /// Relay. The SDK drives NetworkManager host/client start and the transport for us, so there's
    /// no manual Relay/transport wiring or lobby heartbeat to maintain.
    ///
    /// REQUIRES: com.unity.services.multiplayer installed, a linked Unity Cloud project with
    /// Lobby + Relay enabled, and the UGS_LOBBY define. Anonymous auth — no identity provider.
    /// </summary>
    public static class UgsLobbyService
    {
        public const int MaxPlayers = 3;

        private static ISession _current;

        /// <summary>The session we're hosting or have joined; null when not in one. Setting it
        /// (un)subscribes the end-of-life events so a session that dies server-side (host deleted
        /// it, we were removed) clears itself instead of leaving a stale handle behind.</summary>
        public static ISession Current
        {
            get => _current;
            private set
            {
                if (ReferenceEquals(_current, value)) return;
                Unsubscribe(_current);
                _current = value;
                Subscribe(_current);
            }
        }

        public static bool IsSignedIn =>
            UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn;

        public static async Task InitAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        /// <summary>Host a new public session over Relay. NGO hosting starts automatically on success.</summary>
        public static async Task CreateSessionAsync(string sessionName)
        {
            await InitAsync();
            var options = new SessionOptions
            {
                Name = string.IsNullOrWhiteSpace(sessionName) ? "Crystal Tower Run" : sessionName,
                MaxPlayers = MaxPlayers,
            }.WithRelayNetwork();
            Current = await MultiplayerService.Instance.CreateSessionAsync(options);
        }

        /// <summary>List open public sessions for the browser.</summary>
        public static async Task<IList<ISessionInfo>> QuerySessionsAsync()
        {
            await InitAsync();
            var results = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions());
            return results.Sessions;
        }

        /// <summary>Join a session by id — connects the NGO client through Relay automatically.</summary>
        public static async Task JoinSessionAsync(string sessionId)
        {
            await InitAsync();
            Current = await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId);
        }

        /// <summary>
        /// Leave the current session and tear it down. Fire-and-forget wrapper over
        /// <see cref="LeaveOrDeleteAsync"/> for UI handlers.
        /// </summary>
        public static async void Leave() => await LeaveOrDeleteAsync();

        /// <summary>
        /// If we're the host, DELETE the session so the lobby is removed for everyone immediately;
        /// otherwise just leave, freeing our slot. Deleting (rather than leaving) on the host is
        /// deliberate: this game has no NGO host-migration, so a host leaving ends the run — letting
        /// the UGS lobby migrate to a "phantom" host (a client NGO has already dropped) or linger
        /// until its heartbeat times out would leave an un-joinable ghost lobby in the browser.
        /// The session handle is captured before the first await so a racing NetworkManager.Shutdown()
        /// (the Leave button also shuts NGO down) can't null it out from under us; the delete/leave
        /// is a UGS REST call independent of the NGO transport, so it still completes after shutdown.
        /// </summary>
        public static async Task LeaveOrDeleteAsync()
        {
            var session = Current;
            Current = null;
            if (session == null) return;
            try
            {
                if (session.IsHost) await session.AsHost().DeleteAsync();
                else await session.LeaveAsync();
            }
            catch (Exception e) { Debug.LogWarning($"[2CT] Leaving/deleting session failed: {e.Message}"); }
        }

        // =====================================================================
        //  Lifecycle — make sure a lobby never outlives the people in it
        // =====================================================================

        /// <summary>
        /// Registered once at startup: on application quit (incl. exiting Play Mode) tear the session
        /// down. The SDK already auto-calls <c>LeaveAsync</c> on quit, but for a host that only
        /// migrates/orphans the lobby — this upgrades the host's quit-path to a full delete. Best
        /// effort: an abrupt process kill may end before the REST call flushes, in which case the
        /// server-side heartbeat timeout is the backstop.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterLifecycleHooks()
        {
            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
        }

        private static async void OnApplicationQuitting() => await LeaveOrDeleteAsync();

        private static void Subscribe(ISession s)
        {
            if (s == null) return;
            s.Deleted += OnSessionEnded;
            s.RemovedFromSession += OnSessionEnded;
        }

        private static void Unsubscribe(ISession s)
        {
            if (s == null) return;
            s.Deleted -= OnSessionEnded;
            s.RemovedFromSession -= OnSessionEnded;
        }

        /// <summary>
        /// The session ended server-side (the host deleted it, or we were removed). Drop our handle
        /// and shut NGO down so the client falls back to the connect screen instead of sitting in a
        /// dead lobby. Assign the field directly (not the property) to avoid unsubscribing the very
        /// event that's mid-invocation.
        /// </summary>
        private static void OnSessionEnded()
        {
            _current = null;
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm != null && (nm.IsClient || nm.IsServer)) nm.Shutdown();
        }
    }
}
#endif
