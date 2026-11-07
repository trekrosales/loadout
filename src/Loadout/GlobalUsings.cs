// WPF projects leave System.IO out of implicit usings (it clashes with System.Windows.Shapes.Path).
// Code that also uses WPF shapes should write System.Windows.Shapes.Path in full.
global using System.IO;
