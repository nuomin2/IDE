using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Pytools.Models;

namespace Pytools.Services
{
    public class PythonExecutionService : IDisposable
    {
        private Process? _process;
        private bool _disposed;

        public event Action<string>? LineReceived;
        public event Action? ExecutionFinished;
        public event Action<List<VariableEntry>>? VariablesReceived;

        public bool IsRunning { get; private set; }

        public void Execute(string scriptPath)
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
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory
            };

            _process = new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true
            };

            _process.OutputDataReceived += OnOutputLine;
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    LineReceived?.Invoke(e.Data);
            };

            _process.Exited += (_, _) =>
            {
                IsRunning = false;
                try { File.Delete(wrapperPath); } catch { }
                _process.Dispose();
                _process = null;
                ExecutionFinished?.Invoke();
            };

            try
            {
                IsRunning = true;
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
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

            const string marker = "##VAR_DATA_START##";
            int idx = e.Data.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0)
            {
                string json = e.Data[(idx + marker.Length)..];
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

                string wrapper = $@"# -*- coding: utf-8 -*-
import sys, os, json, types, traceback
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
    exec(compile(_cleaned, _user_script, 'exec'), globals())
except Exception:
    traceback.print_exc()

_result = []
for _name, _val in list(globals().items()):
    if _name.startswith('__') and _name.endswith('__'):
        continue
    if _name in ('sys', 'os', 'json', 'types', 'traceback', '_f', '_user_script', '_result', '_name', '_val'):
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
