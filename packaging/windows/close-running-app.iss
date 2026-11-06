// Cloudict cancels its first close while it asynchronously releases audio and Chrome.
// Restart Manager interprets that as ERROR_FAIL_SHUTDOWN and immediately asks for Retry.
// Close the exact installed executable normally and wait before Inno queries files in use.
// This also works when upgrading older Cloudict builds, without changing their shutdown code.

type
  TCloudictWindow = record
    Window: HWND;
    Process: THandle;
    ProcessId: Cardinal;
  end;

var
  CloudictWindows: array of TCloudictWindow;
  CloudictUpgradePath: String;

function EnumCloudictWindows(Callback: LongWord; Param: LongWord): Boolean;
  external 'EnumWindows@user32.dll stdcall';
function CloudictWindowProcess(Window: HWND; var ProcessId: Cardinal): Cardinal;
  external 'GetWindowThreadProcessId@user32.dll stdcall';
function CloudictWindowText(Window: HWND; Text: String; MaxCount: Integer): Integer;
  external 'GetWindowTextW@user32.dll stdcall';
function OpenCloudictProcess(Access: Cardinal; Inherit: Boolean; ProcessId: Cardinal): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function CloudictProcessPath(Process: THandle; Flags: Cardinal; Path: String; var Size: Cardinal): Boolean;
  external 'QueryFullProcessImageNameW@kernel32.dll stdcall';
function WaitForCloudictProcess(Process: THandle; Milliseconds: Cardinal): Cardinal;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseCloudictHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function CollectCloudictWindow(Window: HWND; Param: LongWord): Boolean;
var
  Title, Path: String;
  ProcessId, Size: Cardinal;
  Process: THandle;
  I, Count: Integer;
begin
  Result := True;
  // Both localized main-window titles start with "Cloudict ". Do not filter by
  // native owner: Windows gives the tray-hidden main window an invisible owner.
  // The platform's untitled message window and settings dialogs must stay alone.
  SetLength(Title, 512);
  SetLength(Title, CloudictWindowText(Window, Title, Length(Title)));
  if Pos('Cloudict ', Title) <> 1 then Exit;
  CloudictWindowProcess(Window, ProcessId);
  Process := OpenCloudictProcess($00101000, False, ProcessId); { SYNCHRONIZE | QUERY_LIMITED_INFORMATION }
  if Process = 0 then Exit;
  Size := 32768;
  SetLength(Path, Size);
  if CloudictProcessPath(Process, 0, Path, Size) then
  begin
    SetLength(Path, Size);
    if CompareText(Path, CloudictUpgradePath) = 0 then
    begin
      for I := 0 to GetArrayLength(CloudictWindows) - 1 do
        if CloudictWindows[I].ProcessId = ProcessId then
        begin
          CloseCloudictHandle(Process);
          Exit;
        end;
      Count := GetArrayLength(CloudictWindows);
      SetArrayLength(CloudictWindows, Count + 1);
      CloudictWindows[Count].Window := Window;
      CloudictWindows[Count].Process := Process;
      CloudictWindows[Count].ProcessId := ProcessId;
      Exit;
    end;
  end;
  CloseCloudictHandle(Process);
end;

function CloseCloudictForUpgrade(): String;
var
  I, Elapsed: Integer;
  ProcessId, State: Cardinal;
begin
  Result := '';
  CloudictUpgradePath := ExpandFileName(ExpandConstant('{app}\{#MyAppExeName}'));
  SetArrayLength(CloudictWindows, 0);
  try
    if not EnumCloudictWindows(CreateCallback(@CollectCloudictWindow), 0) then
    begin
      Result := 'Could not check running Cloudict windows. Close Cloudict and try again.';
      Exit;
    end;
    if GetArrayLength(CloudictWindows) = 0 then Exit;

    for I := 1 to ParamCount do
      if CompareText(ParamStr(I), '/NOCLOSEAPPLICATIONS') = 0 then
      begin
        Result := 'Cloudict is running. Close it before continuing the installation.';
        Exit;
      end;

    // Match Inno's existing silent-install behavior: close automatically unless
    // /NOCLOSEAPPLICATIONS was specified, without adding a blocking dialog.
    if not WizardSilent then
      if SuppressibleMsgBox('Cloudict is running. Close it and continue installing?',
        mbConfirmation, MB_YESNO, IDYES) <> IDYES then
      begin
        Result := 'Cloudict was left running. Close it when you are ready to continue.';
        Exit;
      end;

    WizardForm.PreparingLabel.Caption := 'Waiting for Cloudict to close...';
    WizardForm.PreparingLabel.Visible := True;
    WizardForm.Update;
    for I := 0 to GetArrayLength(CloudictWindows) - 1 do
    begin
      // Verify the recorded process is still running and still owns this HWND.
      if WaitForCloudictProcess(CloudictWindows[I].Process, 0) = 258 then
      begin
        CloudictWindowProcess(CloudictWindows[I].Window, ProcessId);
        if ProcessId = CloudictWindows[I].ProcessId then
        begin
          Log('Requesting normal Cloudict shutdown before checking files in use.');
          PostMessage(CloudictWindows[I].Window, $0010, 0, 0); { WM_CLOSE }
        end;
      end;
    end;

    Elapsed := 0;
    for I := 0 to GetArrayLength(CloudictWindows) - 1 do
    begin
      State := WaitForCloudictProcess(CloudictWindows[I].Process, 0);
      while (State = 258) and (Elapsed < 60000) do
      begin
        Sleep(100);
        Elapsed := Elapsed + 100;
        State := WaitForCloudictProcess(CloudictWindows[I].Process, 0);
      end;
      if State <> 0 then
      begin
        Result := 'Cloudict has not finished closing. Close it manually, then try again.';
        Exit;
      end;
    end;
    Log('Cloudict exited; continuing installation without a shutdown retry.');
  finally
    for I := 0 to GetArrayLength(CloudictWindows) - 1 do
      CloseCloudictHandle(CloudictWindows[I].Process);
    SetArrayLength(CloudictWindows, 0);
  end;
end;
