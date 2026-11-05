using System.Windows;
using System.Windows.Threading;
using StorageTerm.Models;
using StorageTerm.Services;

namespace StorageTerm;

public partial class App : System.Windows.Application
{
    private SingleInstance? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        Log.Info($"StorageTerm iniciado. args=[{string.Join(' ', e.Args)}]");

        var options = StartupOptions.Parse(e.Args);
        _singleInstance = new SingleInstance();

        if (!_singleInstance.IsFirstInstance)
        {
            Log.Info("Outra instância já está em execução; pedindo para mostrar a janela.");
            _ = SingleInstance.SignalShowAsync();
            Shutdown(0);
            return;
        }

        try
        {
            var settings = AppSettings.Load();
            Log.Info("Configurações carregadas: " + settings);

            var window = new MainWindow(settings, options);
            MainWindow = window;

            if (options.StartInTray)
            {
                window.Opacity = 0;
            }

            window.Show();

            _singleInstance.StartServer(() =>
                Dispatcher.BeginInvoke(() => window.ShowFromExternalRequest()));
        }
        catch (Exception ex)
        {
            Log.Error("Falha fatal na inicialização.", ex);
            System.Windows.MessageBox.Show(
                "Não foi possível iniciar o StorageTerm.\n\n" + ex.Message +
                "\n\nConsulte o log em:\n" + AppPaths.LogFile,
                "StorageTerm",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Exceção não tratada na interface.", e.Exception);
        e.Handled = true;

        try
        {
            System.Windows.MessageBox.Show(
                "Ocorreu um erro, mas as sessões abertas foram mantidas.\n\n" + e.Exception.Message +
                "\n\nDetalhes em:\n" + AppPaths.LogFile,
                "StorageTerm",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
            // ignore
        }
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Error("Exceção não tratada no processo.", e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error("Exceção não observada em tarefa.", e.Exception);
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info($"StorageTerm finalizado (código {e.ApplicationExitCode}).");

        try
        {
            _singleInstance?.Dispose();
        }
        catch
        {
            // ignore
        }

        base.OnExit(e);
    }
}
