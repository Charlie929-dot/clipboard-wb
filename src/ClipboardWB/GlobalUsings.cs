// UseWPF + UseWindowsForms both inject implicit global usings, so a handful of
// type names (Clipboard, Button, Color, ...) become ambiguous. These aliases
// pin the WPF meaning project-wide; WinForms types are always fully aliased in
// TrayService.cs instead.
global using Clipboard = System.Windows.Clipboard;
global using DataObject = System.Windows.DataObject;
global using TextDataFormat = System.Windows.TextDataFormat;
global using Button = System.Windows.Controls.Button;
global using Color = System.Windows.Media.Color;
