window.library = (() => {
    let connection, dotnet, retryTimer, previousFocus;
    const notifyStatus = online => dotnet?.invokeMethodAsync('NotificationConnectionChanged', online).catch(() => {});
    const closeNavigation = restoreFocus => {
        const navigation = document.getElementById('library-navigation');
        const button = document.querySelector('[aria-controls="library-navigation"]');
        if (!navigation?.classList.contains('mobile-open')) return;
        navigation.classList.remove('mobile-open');
        button?.setAttribute('aria-expanded', 'false');
        if (restoreFocus) button?.focus();
    };
    document.addEventListener('pointerdown', event => {
        const navigation = document.getElementById('library-navigation');
        const button = document.querySelector('[aria-controls="library-navigation"]');
        if (!navigation?.contains(event.target) && !button?.contains(event.target)) closeNavigation(false);
    });
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape') closeNavigation(true);
    });
    window.addEventListener('resize', () => {
        const button = document.querySelector('[aria-controls="library-navigation"]');
        if (button && getComputedStyle(button).display === 'none') closeNavigation(false);
    });
    async function startConnection(current) {
        if (connection !== current || !dotnet) return;
        try { await current.start(); if (connection === current) { notifyStatus(true); dotnet?.invokeMethodAsync('LibraryChanged').catch(() => {}); } }
        catch { notifyStatus(false); retryTimer = setTimeout(() => startConnection(current), 5000); }
    }
    return {
        toggleNavigation(button) {
            const navigation = document.getElementById(button.getAttribute('aria-controls'));
            const open = button.getAttribute('aria-expanded') !== 'true';
            button.setAttribute('aria-expanded', String(open));
            navigation?.classList.toggle('mobile-open', open);
            if (open) navigation?.querySelector('a')?.focus();
        },
        closeNavigation,
        toggleTheme() {
            const dark = document.documentElement.dataset.theme !== 'dark';
            const theme = dark ? 'dark' : 'light';
            document.documentElement.dataset.theme = theme;
            document.documentElement.style.colorScheme = theme;
            try { localStorage.setItem('library-theme', theme); } catch { }
            return dark;
        },
        async connect(reference) {
            window.libraryReady = true;
            dotnet = reference;
            if (!window.signalR || connection) return;
            connection = new signalR.HubConnectionBuilder().withUrl('/hubs/notifications').withAutomaticReconnect().build();
            connection.on('LibraryChanged', () => dotnet?.invokeMethodAsync('LibraryChanged').catch(() => {}));
            connection.onreconnecting(() => notifyStatus(false));
            connection.onreconnected(() => { notifyStatus(true); dotnet?.invokeMethodAsync('LibraryChanged').catch(() => {}); });
            connection.onclose(() => { notifyStatus(false); if (connection && dotnet) retryTimer = setTimeout(() => startConnection(connection), 5000); });
            await startConnection(connection);
        },
        async disconnect() { dotnet = null; clearTimeout(retryTimer); const old = connection; connection = null; if (old) await old.stop(); },
        restoreDialogFocus() { if (previousFocus?.isConnected) previousFocus.focus(); previousFocus = null; },
        focusDialog(element) {
            if (!element.contains(document.activeElement)) previousFocus = document.activeElement;
            element.focus();
            element.addEventListener('keydown', event => {
                if (event.key !== 'Tab') return;
                const buttons = element.querySelectorAll('button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex="0"]');
                if (!buttons.length) { event.preventDefault(); return; }
                const first = buttons[0], last = buttons[buttons.length - 1];
                if (event.shiftKey && (document.activeElement === first || document.activeElement === element)) { event.preventDefault(); last.focus(); }
                else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
            });
        }
    };
})();
