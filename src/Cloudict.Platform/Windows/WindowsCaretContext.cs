using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Cloudict.Abstractions;

namespace Cloudict.Platform.Windows
{
    /// <summary>
    /// Reads the character before the caret in another application, through UI Automation.
    ///
    /// <para>UI Automation is the accessibility interface screen readers use, and its TextPattern is
    /// how a reader knows where the caret is in a document. Notepad, WordPad, Word, Chrome, Edge,
    /// Firefox, VS Code and the standard Windows edit controls all implement it. Asking it is
    /// read-only: no key is sent, the selection is not moved, the clipboard is not touched.</para>
    ///
    /// <para>Applications that do not implement it simply answer "unknown", and the caller falls
    /// back to remembering what it typed itself. Every call is bounded by a short timeout on its
    /// own thread, because an application that is busy can take a long time to answer an
    /// accessibility query, and a word must not wait for that.</para>
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class WindowsCaretContext : ICaretContext
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(600);

        private IUIAutomation _automation;
        private Task<CaretProbe> _inFlight;

        public long ForegroundWindowId()
        {
            try { return GetForegroundWindow().ToInt64(); }
            catch { return 0; }
        }

        public CaretProbe ProbeCharBeforeCaret()
        {
            try
            {
                // Never ask our own process: the answer would have to come from Cloudict's UI
                // thread, which may be the very thread waiting for it.
                var foreground = GetForegroundWindow();
                if (foreground == IntPtr.Zero) return CaretProbe.Unknown;
                GetWindowThreadProcessId(foreground, out var pid);
                if (pid == (uint)Environment.ProcessId) return CaretProbe.Unknown;

                // One probe at a time. If an earlier one is still waiting on a slow application,
                // wait for it to finish and then ask afresh — its answer may describe a caret that
                // has since moved. Giving up at once here used to turn a warm-up probe still in
                // flight into an "unknown" for the first real question.
                var previous = Volatile.Read(ref _inFlight);
                if (previous != null && !previous.IsCompleted && !previous.Wait(Timeout))
                    return CaretProbe.Unknown;

                var task = Task.Run(Probe);
                Volatile.Write(ref _inFlight, task);

                return task.Wait(Timeout) ? task.Result : CaretProbe.Unknown;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsCaretContext] probe: {ex.Message}");
                return CaretProbe.Unknown;
            }
        }

        private CaretProbe Probe()
        {
            object patternObject = null;
            IUIAutomationElement element = null;
            IUIAutomationTextRangeArray selection = null;
            IUIAutomationTextRange range = null;
            IUIAutomationTextRange before = null;

            try
            {
                _automation ??= (IUIAutomation)new CUIAutomation();

                if (_automation.GetFocusedElement(out element) != 0 || element == null) return CaretProbe.Unknown;
                if (element.GetCurrentPattern(UIA_TextPatternId, out patternObject) != 0 || patternObject is not IUIAutomationTextPattern pattern)
                    return CaretProbe.Unknown;

                if (pattern.GetSelection(out selection) != 0 || selection == null) return CaretProbe.Unknown;
                if (selection.get_Length(out var count) != 0 || count < 1) return CaretProbe.Unknown;
                if (selection.GetElement(0, out range) != 0 || range == null) return CaretProbe.Unknown;

                // A copy of the caret (or selection), collapsed onto its start: typing replaces a
                // selection, so what matters is what comes before it.
                if (range.Clone(out before) != 0 || before == null) return CaretProbe.Unknown;
                if (before.MoveEndpointByRange(TextPatternRangeEndpoint_End, range, TextPatternRangeEndpoint_Start) != 0)
                    return CaretProbe.Unknown;

                // Stretch it one character backwards and read that character.
                if (before.MoveEndpointByUnit(TextPatternRangeEndpoint_Start, TextUnit_Character, -1, out var moved) != 0)
                    return CaretProbe.Unknown;
                if (moved == 0) return CaretProbe.AtStart;

                if (before.GetText(8, out var text) != 0 || string.IsNullOrEmpty(text)) return CaretProbe.Unknown;

                return CaretProbe.After(text[^1]);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsCaretContext] UI Automation: {ex.Message}");
                return CaretProbe.Unknown;
            }
            finally
            {
                Release(before);
                Release(range);
                Release(selection);
                Release(patternObject);
                Release(element);
            }
        }

        private static void Release(object com)
        {
            try { if (com != null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com); }
            catch { /* already gone */ }
        }

        #region Interop

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private const int UIA_TextPatternId = 10014;
        private const int TextPatternRangeEndpoint_Start = 0;
        private const int TextPatternRangeEndpoint_End = 1;
        private const int TextUnit_Character = 0;

        [ComImport, Guid("ff48dba4-60ef-4201-aa87-54103eef594e")]
        private class CUIAutomation { }

        // Only the members that are called are named; every slot before them is declared, in
        // order, so the vtable lines up with UIAutomationClient.h. PreserveSig keeps failures as
        // HRESULTs to check rather than exceptions thrown from inside another application's answer.

        [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomation
        {
            void _CompareElements();
            void _CompareRuntimeIds();
            void _GetRootElement();
            void _ElementFromHandle();
            void _ElementFromPoint();
            [PreserveSig] int GetFocusedElement(out IUIAutomationElement element);
        }

        [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationElement
        {
            void _SetFocus();
            void _GetRuntimeId();
            void _FindFirst();
            void _FindAll();
            void _FindFirstBuildCache();
            void _FindAllBuildCache();
            void _BuildUpdatedCache();
            void _GetCurrentPropertyValue();
            void _GetCurrentPropertyValueEx();
            void _GetCachedPropertyValue();
            void _GetCachedPropertyValueEx();
            void _GetCurrentPatternAs();
            void _GetCachedPatternAs();
            [PreserveSig] int GetCurrentPattern(int patternId, [MarshalAs(UnmanagedType.IUnknown)] out object pattern);
        }

        [ComImport, Guid("32eba289-3583-42c9-9c59-3b6d9a1e9b6a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationTextPattern
        {
            void _RangeFromPoint();
            void _RangeFromChild();
            [PreserveSig] int GetSelection(out IUIAutomationTextRangeArray ranges);
        }

        [ComImport, Guid("ce4ae76a-e717-4c98-81ea-47371d028eb6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationTextRangeArray
        {
            [PreserveSig] int get_Length(out int length);
            [PreserveSig] int GetElement(int index, out IUIAutomationTextRange element);
        }

        [ComImport, Guid("a543cc6a-f4ae-494b-8239-c814481187a8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationTextRange
        {
            [PreserveSig] int Clone(out IUIAutomationTextRange clone);
            void _Compare();
            void _CompareEndpoints();
            void _ExpandToEnclosingUnit();
            void _FindAttribute();
            void _FindText();
            void _GetAttributeValue();
            void _GetBoundingRectangles();
            void _GetEnclosingElement();
            [PreserveSig] int GetText(int maxLength, [MarshalAs(UnmanagedType.BStr)] out string text);
            void _Move();
            [PreserveSig] int MoveEndpointByUnit(int endpoint, int unit, int count, out int moved);
            [PreserveSig] int MoveEndpointByRange(int srcEndPoint, IUIAutomationTextRange range, int targetEndPoint);
        }

        #endregion
    }
}
