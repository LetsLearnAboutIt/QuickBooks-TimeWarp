using Serilog.Core;
using Serilog.Events;

namespace QB_TimeWarp.UI.ViewModels
{
    /// <summary>
    /// Custom Serilog sink that forwards log messages to the MainViewModel's activity log.
    /// Wire this into the Serilog pipeline when running in GUI mode.
    /// </summary>
    public class GuiLogSink : ILogEventSink
    {
        private readonly MainViewModel _viewModel;

        public GuiLogSink(MainViewModel viewModel)
        {
            _viewModel = viewModel;
        }

        public void Emit(LogEvent logEvent)
        {
            var message = logEvent.RenderMessage();
            var level = logEvent.Level switch
            {
                LogEventLevel.Error => "ERR",
                LogEventLevel.Warning => "WRN",
                LogEventLevel.Information => "INF",
                LogEventLevel.Debug => "DBG",
                _ => "   "
            };

            _viewModel.AppendLog($"[{level}] {message}");

            if (logEvent.Exception != null)
            {
                _viewModel.AppendLog($"  Exception: {logEvent.Exception.Message}");
            }
        }
    }
}
