using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Pytools.Models;
using Pytools.Models.Simulation;

namespace Pytools.Services
{
    public class PythonExecutionService : IDisposable
    {
        private Process? _process;
        private bool _disposed;

        public event Action<string>? LineReceived;
        public event Action? ExecutionFinished;
        public event Action<List<VariableEntry>>? VariablesReceived;
        public event Action<byte[]>? PlotReceived;

        public bool IsRunning { get; private set; }

        public async Task ExecuteAsync(string scriptPath)
        {
            if (IsRunning) return;

            string directory = Path.GetDirectoryName(scriptPath) ?? "";
            string scriptName = Path.GetFileName(scriptPath);

            string? pythonPath = FindPython();
            if (pythonPath == null)
            {
                LineReceived?.Invoke("Python interpreter not found on system PATH.");
                ExecutionFinished?.Invoke();
                return;
            }

            string? wrapperPath = BuildWrapperScript(scriptPath, scriptName, directory);
            if (wrapperPath == null)
            {
                LineReceived?.Invoke("Failed to create execution wrapper.");
                ExecutionFinished?.Invoke();
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"\"{wrapperPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory
            };

            _process = new Process { StartInfo = psi };

            _process.OutputDataReceived += OnOutputLine;
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    LineReceived?.Invoke(e.Data);
            };

            try
            {
                IsRunning = true;
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                await _process.WaitForExitAsync();

                IsRunning = false;
                try { File.Delete(wrapperPath); } catch { }
                _process.Dispose();
                _process = null;
                ExecutionFinished?.Invoke();
            }
            catch (Exception ex)
            {
                IsRunning = false;
                try { File.Delete(wrapperPath); } catch { }
                LineReceived?.Invoke($"Failed to start Python: {ex.Message}");
                ExecutionFinished?.Invoke();
            }
        }

        private void OnOutputLine(object? sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;

            const string simMarker = "##SIM_START##";
            int simIdx = e.Data.IndexOf(simMarker, StringComparison.Ordinal);
            if (simIdx >= 0)
            {
                string json = e.Data[(simIdx + simMarker.Length)..];
                int simEndIdx = json.IndexOf("##SIM_END##", StringComparison.Ordinal);
                if (simEndIdx >= 0)
                {
                    json = json[..simEndIdx].Trim();
                    string responseJson;
                    try
                    {
                        var request = JsonSerializer.Deserialize<SimulationRequest>(json);
                        if (request == null)
                        {
                            responseJson = "{\"status\":\"error\",\"message\":\"JSON 解析失败：请求为空。\"}";
                            WriteStdin(responseJson);
                        }
                        else
                        {
                            string? error = TopologyValidator.Validate(request);
                            if (error != null)
                            {
                                responseJson = JsonSerializer.Serialize(new { status = "error", message = error });
                                WriteStdin(responseJson);
                            }
                            else
                            {
                                // 校验通过 → 后台运行仿真引擎
                                _ = Task.Run(() =>
                                {
                                    try
                                    {
                                        var engine = new SimulatorEngine(request);
                                        var result = engine.Run();
                                        string resultJson = JsonSerializer.Serialize(result);
                                        WriteStdin(resultJson);
                                    }
                                    catch (Exception ex)
                                    {
                                        WriteStdin(JsonSerializer.Serialize(
                                            new { status = "error", message = $"仿真引擎异常：{ex.Message}" }));
                                    }
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        responseJson = JsonSerializer.Serialize(new { status = "error", message = $"JSON 解析异常：{ex.Message}" });
                        WriteStdin(responseJson);
                    }
                }
                return;
            }

            const string plotMarker = "##PLOT_DATA_START##";
            int plotIdx = e.Data.IndexOf(plotMarker, StringComparison.Ordinal);
            if (plotIdx >= 0)
            {
                string b64 = e.Data[(plotIdx + plotMarker.Length)..];
                int plotEndIdx = b64.IndexOf("##PLOT_DATA_END##", StringComparison.Ordinal);
                if (plotEndIdx >= 0)
                {
                    b64 = b64[..plotEndIdx];
                    try
                    {
                        byte[] data = Convert.FromBase64String(b64);
                        PlotReceived?.Invoke(data);
                    }
                    catch { }
                }
                return;
            }

            const string varMarker = "##VAR_DATA_START##";
            int varIdx = e.Data.IndexOf(varMarker, StringComparison.Ordinal);
            if (varIdx >= 0)
            {
                string json = e.Data[(varIdx + varMarker.Length)..];
                int endIdx = json.IndexOf("##VAR_DATA_END##", StringComparison.Ordinal);
                if (endIdx >= 0)
                {
                    json = json[..endIdx];
                    try
                    {
                        var entries = JsonSerializer.Deserialize<List<JsonElement>>(json);
                        if (entries != null)
                        {
                            var vars = entries.Select(j => new VariableEntry
                            {
                                Name = j.GetProperty("n").GetString() ?? "",
                                Type = j.GetProperty("t").GetString() ?? "",
                                Size = j.GetProperty("s").GetString() ?? "",
                                Value = j.GetProperty("v").GetString() ?? "",
                                TypeColor = j.GetProperty("c").GetString() ?? "#455a64"
                            }).ToList();
                            VariablesReceived?.Invoke(vars);
                        }
                    }
                    catch { }
                }
                return;
            }

            LineReceived?.Invoke(e.Data);
        }

        private void WriteStdin(string json)
        {
            try
            {
                _process?.StandardInput.WriteLine(json);
                _process?.StandardInput.Flush();
            }
            catch { }
        }

        public void Stop()
        {
            if (_process != null && !_process.HasExited)
            {
                try { _process.Kill(); } catch { }
                _process.Dispose();
                _process = null;
            }
            IsRunning = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            LineReceived = null;
            ExecutionFinished = null;
            VariablesReceived = null;
            PlotReceived = null;
        }

        private static string? FindPython()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(3000);
                return p?.ExitCode == 0 ? "python" : null;
            }
            catch { return null; }
        }

        private static string? BuildWrapperScript(string scriptPath, string scriptName, string workingDir)
        {
            try
            {
                string escapedPath = scriptPath.Replace("\\", "\\\\");
                string escapedDir = workingDir.Replace("\\", "\\\\");
                string sdkDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SDK");
                string escapedSdkDir = sdkDir.Replace("\\", "\\\\");

                string wrapper = $@"# -*- coding: utf-8 -*-
import sys, os, json, types, traceback

_sdk_path = r'{escapedSdkDir}'
if _sdk_path not in sys.path:
    sys.path.insert(0, _sdk_path)

os.chdir(r'{escapedDir}')
_user_script = r'{escapedPath}'

try:
    with open(_user_script, encoding='utf-8-sig') as _f:
        _raw = _f.read()
    _lines = _raw.splitlines(keepends=True)
    _cleaned = ''.join(
        '\n' if 'coding:' in line or 'coding=' in line else line
        for line in _lines
    )
    _ide_boot_snapshots = set(list(globals().keys()))
    _ide_boot_snapshots.add('_ide_boot_snapshots')

    try:
        import base64 as _base64, io as _io
        import matplotlib
        matplotlib.use('Agg')
        import matplotlib.pyplot as _plt

        def _pytools_show(*_args, **_kwargs):
            for _fignum in _plt.get_fignums():
                _fig = _plt.figure(_fignum)
                _buf = _io.BytesIO()
                _fig.savefig(_buf, format='png', dpi=100, bbox_inches='tight')
                _buf.seek(0)
                _b64 = _base64.b64encode(_buf.read()).decode('ascii')
                print('##PLOT_DATA_START##' + _b64 + '##PLOT_DATA_END##')
                _buf.close()
                _plt.close(_fig)
        _plt.show = _pytools_show
    except Exception:
        pass

    exec(compile(_cleaned, _user_script, 'exec'), globals())
except Exception:
    traceback.print_exc()

_result = []
for _name, _val in list(globals().items()):
    if _name.startswith('__') and _name.endswith('__'):
        continue
    if _name in ('sys', 'os', 'json', 'types', 'traceback', '_f', '_user_script', '_result', '_name', '_val'):
        continue
    if _name in _ide_boot_snapshots:
        continue
    _t = type(_val)
    if _t in (types.ModuleType, types.FunctionType, types.BuiltinFunctionType):
        continue

    _tname = _t.__name__
    _color = '#455a64'
    if isinstance(_val, bool):
        _color = '#7b1fa2'
    elif isinstance(_val, (int, float)):
        _color = '#b56a24'
    elif isinstance(_val, str):
        _color = '#388e3c'
    elif isinstance(_val, (list, dict, tuple, set)):
        _color = '#1565c0'

    try:
        if isinstance(_val, (list, tuple, set, dict, str)):
            _size = str(len(_val))
        else:
            _size = '1'
        _vstr = repr(_val)
        if len(_vstr) > 80:
            _vstr = _vstr[:77] + '...'
    except:
        _size = '?'
        _vstr = '?'

    _result.append({{'n': _name, 't': _tname, 's': _size, 'v': _vstr, 'c': _color}})

print('##VAR_DATA_START##' + json.dumps(_result) + '##VAR_DATA_END##')
";
                string wrapperPath = Path.Combine(Path.GetTempPath(), $"pytools_run_{Guid.NewGuid():N}.py");
                File.WriteAllText(wrapperPath, wrapper);
                return wrapperPath;
            }
            catch { return null; }
        }
    }
}
