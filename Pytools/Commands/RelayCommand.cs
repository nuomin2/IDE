using System;
using System.Windows.Input;

namespace Pytools.Commands
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;  ///函数变量 返回void ，参数为object类型（可以是任意类型）
        private readonly Predicate<object?>? _canExecute;///函数变量 返回bool，参数为object类型（可以是任意类型），用于判断命令是否可执行

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));//A ?? B 表示如果A不为null，则返回A，否则返回B
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);

        public void Execute(object? parameter) => _execute(parameter);

        /// <summary>
        /// 事件触发器，用于通知命令的可执行状态发生了变化。
        /// </summary>
        public event EventHandler? CanExecuteChanged
        {
            /// <summary>
            /// 添加事件处理程序到 CommandManager.RequerySuggested。
            /// </summary>
            add => CommandManager.RequerySuggested += value;

            /// <summary>
            /// 从 CommandManager.RequerySuggested 中移除事件处理程序。
            /// </summary>
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
