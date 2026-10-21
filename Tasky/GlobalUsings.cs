// Resolves naming conflicts between System.Windows (WPF) and
// System.Windows.Forms. WPF types win by default project-wide.
// Code that genuinely needs WinForms types must use fully-qualified names.

global using Application         = System.Windows.Application;
global using Button              = System.Windows.Controls.Button;
global using Clipboard           = System.Windows.Clipboard;
global using Cursors             = System.Windows.Input.Cursors;
global using DataFormats         = System.Windows.DataFormats;
global using Image               = System.Windows.Controls.Image;
global using HorizontalAlignment = System.Windows.HorizontalAlignment;
global using OpenFileDialog      = Microsoft.Win32.OpenFileDialog;
global using Brush          = System.Windows.Media.Brush;
global using Pen            = System.Windows.Media.Pen;
global using Brushes        = System.Windows.Media.Brushes;
global using Color          = System.Windows.Media.Color;
global using DataObject     = System.Windows.DataObject;
global using DragDropEffects = System.Windows.DragDropEffects;
global using DragEventArgs  = System.Windows.DragEventArgs;
global using KeyEventArgs   = System.Windows.Input.KeyEventArgs;
global using MessageBox     = System.Windows.MessageBox;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using Point          = System.Windows.Point;
global using RadioButton    = System.Windows.Controls.RadioButton;
global using ComboBox       = System.Windows.Controls.ComboBox;
global using Orientation    = System.Windows.Controls.Orientation;
global using ColorConverter = System.Windows.Media.ColorConverter;
global using Rectangle      = System.Windows.Shapes.Rectangle;
global using FontFamily     = System.Windows.Media.FontFamily;
