using Avalonia.Media;

namespace JustModIt.Manager.Ui.Theme;

/// <summary>
/// The Lucide icons (ISC) the window draws, as 24x24 stroke geometries for <see cref="Controls.Icon"/>, named after
/// Lucide's own (<c>circle-arrow-up</c> is <see cref="CircleArrowUp"/>; <see cref="Gamepad"/> is <c>gamepad-2</c>,
/// <see cref="Trash"/> <c>trash-2</c>, <see cref="Undo"/> <c>undo-2</c>, <see cref="Columns"/> <c>columns-2</c> and
/// <see cref="Volume"/> <c>volume-2</c>). XAML takes them with <c>{x:Static theme:Icons.Download}</c>.
/// </summary>
public static class Icons
{
    public static Geometry AppWindow => field ??= Parse("M4 4h16a2 2 0 0 1 2 2v12a2 2 0 0 1 -2 2h-16a2 2 0 0 1 -2 -2v-12a2 2 0 0 1 2 -2z M10 4v4 M2 8h20 M6 4v4");

    public static Geometry Archive => field ??= Parse("M3 3h18a1 1 0 0 1 1 1v3a1 1 0 0 1 -1 1h-18a1 1 0 0 1 -1 -1v-3a1 1 0 0 1 1 -1z M4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8 M10 12h4");

    public static Geometry ArrowDown => field ??= Parse("M12 5v14 M19 12l-7 7-7-7");

    public static Geometry ArrowDownWideNarrow => field ??= Parse("M3 16l4 4 4-4 M7 20V4 M11 4h10 M11 8h7 M11 12h4");

    public static Geometry ArrowLeft => field ??= Parse("M12 19l-7-7 7-7 M19 12H5");

    public static Geometry ArrowRight => field ??= Parse("M5 12h14 M12 5l7 7-7 7");

    public static Geometry ArrowUp => field ??= Parse("M5 12l7-7 7 7 M12 19V5");

    public static Geometry ArrowUpRight => field ??= Parse("M7 7h10v10 M7 17 17 7");

    public static Geometry Ban => field ??= Parse("M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0z M4.9 4.9l14.2 14.2");

    public static Geometry BatteryMedium => field ??= Parse("M4 6h12a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2v-8a2 2 0 0 1 2 -2z M22 14v-4 M6 10v4 M10 10v4");

    public static Geometry Bell => field ??= Parse("M10.268 21a2 2 0 0 0 3.464 0 M3.262 15.326A1 1 0 0 0 4 17h16a1 1 0 0 0 .74-1.673C19.41 13.956 18 12.499 18 8A6 6 0 0 0 6 8c0 4.499-1.411 5.956-2.738 7.326");

    public static Geometry Bug => field ??= Parse("M8 2l1.88 1.88 M14.12 3.88 16 2 M9 7.13v-1a3.003 3.003 0 1 1 6 0v1 M12 20c-3.3 0-6-2.7-6-6v-3a4 4 0 0 1 4-4h4a4 4 0 0 1 4 4v3c0 3.3-2.7 6-6 6 M12 20v-9 M6.53 9C4.6 8.8 3 7.1 3 5 M6 13H2 M3 21c0-2.1 1.7-3.9 3.8-4 M20.97 5c0 2.1-1.6 3.8-3.5 4 M22 13h-4 M17.2 17c2.1.1 3.8 1.9 3.8 4");

    public static Geometry ChartPie => field ??= Parse("M21 12c.552 0 1.005-.449.95-.998a10 10 0 0 0-8.953-8.951c-.55-.055-.998.398-.998.95v8a1 1 0 0 0 1 1z M21.21 15.89A10 10 0 1 1 8 2.83");

    public static Geometry Check => field ??= Parse("M20 6 9 17l-5-5");

    public static Geometry CheckCheck => field ??= Parse("M18 6 7 17l-5-5 M22 10l-7.5 7.5L13 16");

    public static Geometry ChevronDown => field ??= Parse("M6 9l6 6 6-6");

    public static Geometry ChevronLeft => field ??= Parse("M15 18l-6-6 6-6");

    public static Geometry ChevronRight => field ??= Parse("M9 18l6-6-6-6");

    public static Geometry ChevronUp => field ??= Parse("M18 15l-6-6-6 6");

    public static Geometry CircleArrowUp => field ??= Parse("M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0z M16 12l-4-4-4 4 M12 16V8");

    public static Geometry CircleCheck => field ??= Parse("M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0z M9 12l2 2 4-4");

    public static Geometry CircleHelp => field ??= Parse("M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0z M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3 M12 17h.01");

    public static Geometry CircleMinus => field ??= Parse("M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0z M8 12h8");

    public static Geometry CircleX => field ??= Parse("M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0z M15 9l-6 6 M9 9l6 6");

    public static Geometry Clock => field ??= Parse("M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0z M12 6v6l4 2");

    public static Geometry CodeXml => field ??= Parse("M18 16l4-4-4-4 M6 8l-4 4 4 4 M14.5 4l-5 16");

    public static Geometry Coffee => field ??= Parse("M10 2v2 M14 2v2 M16 8a1 1 0 0 1 1 1v8a4 4 0 0 1-4 4H7a4 4 0 0 1-4-4V9a1 1 0 0 1 1-1h14a4 4 0 1 1 0 8h-1 M6 2v4");

    public static Geometry Columns => field ??= Parse("M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-14a2 2 0 0 1 2 -2z M12 3v18");

    public static Geometry Copy => field ??= Parse("M10 8h10a2 2 0 0 1 2 2v10a2 2 0 0 1 -2 2h-10a2 2 0 0 1 -2 -2v-10a2 2 0 0 1 2 -2z M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2");

    public static Geometry Download => field ??= Parse("M12 15V3 M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M7 10l5 5 5-5");

    public static Geometry EllipsisVertical => field ??= Parse("M11 12a1 1 0 1 0 2 0a1 1 0 1 0 -2 0z M11 5a1 1 0 1 0 2 0a1 1 0 1 0 -2 0z M11 19a1 1 0 1 0 2 0a1 1 0 1 0 -2 0z");

    public static Geometry Eraser => field ??= Parse("M21 21H8a2 2 0 0 1-1.42-.587l-3.994-3.999a2 2 0 0 1 0-2.828l10-10a2 2 0 0 1 2.829 0l5.999 6a2 2 0 0 1 0 2.828L12.834 21 M5.082 11.09l8.828 8.828");

    public static Geometry ExternalLink => field ??= Parse("M15 3h6v6 M10 14 21 3 M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6");

    public static Geometry Eye => field ??= Parse("M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0 M9 12a3 3 0 1 0 6 0a3 3 0 1 0 -6 0z");

    public static Geometry EyeOff => field ??= Parse("M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49 M14.084 14.158a3 3 0 0 1-4.242-4.242 M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143 M2 2l20 20");

    public static Geometry File => field ??= Parse("M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z M14 2v4a2 2 0 0 0 2 2h4");

    public static Geometry FileArchive => field ??= Parse("M10 12v-1 M10 18v-2 M10 7V6 M14 2v4a2 2 0 0 0 2 2h4 M15.5 22H18a2 2 0 0 0 2-2V7l-5-5H6a2 2 0 0 0-2 2v16a2 2 0 0 0 .274 1.01 M8 20a2 2 0 1 0 4 0a2 2 0 1 0 -4 0z");

    public static Geometry FileCode => field ??= Parse("M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z M14 2v4a2 2 0 0 0 2 2h4 M10 12.5 8 15l2 2.5 M14 12.5l2 2.5-2 2.5");

    public static Geometry FileDiff => field ??= Parse("M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z M14 2v4a2 2 0 0 0 2 2h4 M9 10h6 M12 13V7 M9 17h6");

    public static Geometry FilePen => field ??= Parse("M18 5l-2.414-2.414A2 2 0 0 0 14.172 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2 M21.378 12.626a1 1 0 0 0-3.004-3.004l-4.01 4.012a2 2 0 0 0-.506.854l-.837 2.87a.5.5 0 0 0 .62.62l2.87-.837a2 2 0 0 0 .854-.506z M8 18h1");

    public static Geometry FileX => field ??= Parse("M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z M14 2v4a2 2 0 0 0 2 2h4 M14.5 12.5l-5 5 M9.5 12.5l5 5");

    public static Geometry Folder => field ??= Parse("M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z");

    public static Geometry FolderDown => field ??= Parse("M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z M12 10v6 M15 13l-3 3-3-3");

    public static Geometry FolderInput => field ??= Parse("M2 9V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H20a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-1 M2 13h10 M9 16l3-3-3-3");

    public static Geometry FolderOpen => field ??= Parse("M6 14l1.5-2.9A2 2 0 0 1 9.24 10H20a2 2 0 0 1 1.94 2.5l-1.54 6a2 2 0 0 1-1.95 1.5H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H18a2 2 0 0 1 2 2v2");

    public static Geometry FolderPlus => field ??= Parse("M12 10v6 M9 13h6 M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z");

    public static Geometry FolderSearch => field ??= Parse("M10.7 20H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H20a2 2 0 0 1 2 2v4.1 M21 21l-1.9-1.9 M14 17a3 3 0 1 0 6 0a3 3 0 1 0 -6 0z");

    public static Geometry Funnel => field ??= Parse("M10 20a1 1 0 0 0 .553.895l2 1A1 1 0 0 0 14 21v-7a2 2 0 0 1 .517-1.341L21.74 4.67A1 1 0 0 0 21 3H3a1 1 0 0 0-.742 1.67l7.225 7.989A2 2 0 0 1 10 14z");

    public static Geometry Gamepad => field ??= Parse("M6 11h4 M8 9v4 M15 12h.01 M18 10h.01 M17.32 5H6.68a4 4 0 0 0-3.978 3.59c-.006.052-.01.101-.017.152C2.604 9.416 2 14.456 2 16a3 3 0 0 0 3 3c1 0 1.5-.5 2-1l1.414-1.414A2 2 0 0 1 9.828 16h4.344a2 2 0 0 1 1.414.586L17 18c.5.5 1 1 2 1a3 3 0 0 0 3-3c0-1.545-.604-6.584-.685-7.258-.007-.05-.011-.1-.017-.151A4 4 0 0 0 17.32 5z");

    public static Geometry Globe => field ??= Parse("M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0z M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20 M2 12h20");

    public static Geometry GripVertical => field ??= Parse("M8 12a1 1 0 1 0 2 0a1 1 0 1 0 -2 0z M8 5a1 1 0 1 0 2 0a1 1 0 1 0 -2 0z M8 19a1 1 0 1 0 2 0a1 1 0 1 0 -2 0z M14 12a1 1 0 1 0 2 0a1 1 0 1 0 -2 0z M14 5a1 1 0 1 0 2 0a1 1 0 1 0 -2 0z M14 19a1 1 0 1 0 2 0a1 1 0 1 0 -2 0z");

    public static Geometry Hand => field ??= Parse("M18 11V6a2 2 0 0 0-2-2a2 2 0 0 0-2 2 M14 10V4a2 2 0 0 0-2-2a2 2 0 0 0-2 2v2 M10 10.5V6a2 2 0 0 0-2-2a2 2 0 0 0-2 2v8 M18 8a2 2 0 1 1 4 0v6a8 8 0 0 1-8 8h-2c-2.8 0-4.5-.86-5.99-2.34l-3.6-3.6a2 2 0 0 1 2.83-2.82L7 15");

    public static Geometry HardDrive => field ??= Parse("M22 12L2 12 M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z M6 16L6.01 16 M10 16L10.01 16");

    public static Geometry Hourglass => field ??= Parse("M5 22h14 M5 2h14 M17 22v-4.172a2 2 0 0 0-.586-1.414L12 12l-4.414 4.414A2 2 0 0 0 7 17.828V22 M7 2v4.172a2 2 0 0 0 .586 1.414L12 12l4.414-4.414A2 2 0 0 0 17 6.172V2");

    public static Geometry Image => field ??= Parse("M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-14a2 2 0 0 1 2 -2z M7 9a2 2 0 1 0 4 0a2 2 0 1 0 -4 0z M21 15l-3.086-3.086a2 2 0 0 0-2.828 0L6 21");

    public static Geometry Info => field ??= Parse("M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0z M12 16v-4 M12 8h.01");

    public static Geometry Languages => field ??= Parse("M5 8l6 6 M4 14l6-6 2-3 M2 5h12 M7 2h1 M22 22l-5-10-5 10 M14 18h6");

    public static Geometry Layers => field ??= Parse("M12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83z M2 12a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 12 M2 17a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 17");

    public static Geometry Library => field ??= Parse("M16 6l4 14 M12 6v14 M8 8v12 M4 4v16");

    public static Geometry ListChecks => field ??= Parse("M13 5h8 M13 12h8 M13 19h8 M3 17l2 2 4-4 M3 7l2 2 4-4");

    public static Geometry ListOrdered => field ??= Parse("M10 12h11 M10 18h11 M10 6h11 M4 10h2 M4 6h1v4 M6 18H4c0-1 2-2 2-3s-1-1.5-2-1");

    public static Geometry LoaderCircle => field ??= Parse("M21 12a9 9 0 1 1-6.219-8.56");

    public static Geometry Minus => field ??= Parse("M5 12h14");

    public static Geometry Monitor => field ??= Parse("M4 3h16a2 2 0 0 1 2 2v10a2 2 0 0 1 -2 2h-16a2 2 0 0 1 -2 -2v-10a2 2 0 0 1 2 -2z M12 17v4 M8 21h8");

    public static Geometry MonitorCheck => field ??= Parse("M9 10l2 2 4-4 M4 3h16a2 2 0 0 1 2 2v10a2 2 0 0 1 -2 2h-16a2 2 0 0 1 -2 -2v-10a2 2 0 0 1 2 -2z M12 17v4 M8 21h8");

    public static Geometry MonitorDown => field ??= Parse("M12 13V7 M15 10l-3 3-3-3 M4 3h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M12 17v4 M8 21h8");

    public static Geometry MousePointer => field ??= Parse("M12.586 12.586 19 19 M3.688 3.037a.497.497 0 0 0-.651.651l6.5 15.999a.501.501 0 0 0 .947-.062l1.569-6.083a2 2 0 0 1 1.448-1.479l6.124-1.579a.5.5 0 0 0 .063-.947z");

    public static Geometry Package => field ??= Parse("M11 21.73a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73z M12 22V12 M3.3 7l7.703 4.734a2 2 0 0 0 1.994 0L20.7 7 M7.5 4.27l9 5.15");

    public static Geometry PanelBottomClose => field ??= Parse("M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-14a2 2 0 0 1 2 -2z M3 15h18 M15 8l-3 3-3-3");

    public static Geometry PanelsTopLeft => field ??= Parse("M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-14a2 2 0 0 1 2 -2z M3 9h18 M9 21V9");

    public static Geometry Pencil => field ??= Parse("M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z M15 5l4 4");

    public static Geometry Play => field ??= Parse("M6 3 20 12 6 21 6 3z");

    public static Geometry Plus => field ??= Parse("M5 12h14 M12 5v14");

    public static Geometry Power => field ??= Parse("M12 2v10 M18.4 6.6a9 9 0 1 1-12.77.04");

    public static Geometry RefreshCw => field ??= Parse("M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8 M21 3v5h-5 M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16 M8 16H3v5");

    public static Geometry Rocket => field ??= Parse("M4.5 16.5c-1.5 1.26-2 5-2 5s3.74-.5 5-2c.71-.84.7-2.13-.09-2.91a2.18 2.18 0 0 0-2.91-.09z M12 15l-3-3a22 22 0 0 1 2-3.95A12.88 12.88 0 0 1 22 2c0 2.72-.78 7.5-6 11a22.35 22.35 0 0 1-4 2z M9 12H4s.55-3.03 2-4c1.62-1.08 5 0 5 0 M12 15v5s3.03-.55 4-2c1.08-1.62 0-5 0-5");

    public static Geometry RotateCcw => field ??= Parse("M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8 M3 3v5h5");

    public static Geometry RotateCw => field ??= Parse("M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1 6.74 2.74L21 8 M21 3v5h-5");

    public static Geometry Save => field ??= Parse("M15.2 3a2 2 0 0 1 1.4.6l3.8 3.8a2 2 0 0 1 .6 1.4V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M17 21v-7a1 1 0 0 0-1-1H8a1 1 0 0 0-1 1v7 M7 3v4a1 1 0 0 0 1 1h7");

    public static Geometry Scale => field ??= Parse("M16 16l3-8 3 8c-.87.65-1.92 1-3 1s-2.13-.35-3-1Z M2 16l3-8 3 8c-.87.65-1.92 1-3 1s-2.13-.35-3-1Z M7 21h10 M12 3v18 M3 7h2c2 0 5-1 7-2 2 1 5 2 7 2h2");

    public static Geometry ScrollText => field ??= Parse("M15 12h-5 M15 8h-5 M19 17V5a2 2 0 0 0-2-2H4 M8 21h12a2 2 0 0 0 2-2v-1a1 1 0 0 0-1-1H11a1 1 0 0 0-1 1v1a2 2 0 1 1-4 0V5a2 2 0 1 0-4 0v2a1 1 0 0 0 1 1h3");

    public static Geometry Search => field ??= Parse("M3 11a8 8 0 1 0 16 0a8 8 0 1 0 -16 0z M21 21l-4.34-4.34");

    public static Geometry SearchX => field ??= Parse("M13.5 8.5l-5 5 M8.5 8.5l5 5 M3 11a8 8 0 1 0 16 0a8 8 0 1 0 -16 0z M21 21l-4.3-4.3");

    public static Geometry Send => field ??= Parse("M14.536 21.686a.5.5 0 0 0 .937-.024l6.5-19a.496.496 0 0 0-.635-.635l-19 6.5a.5.5 0 0 0-.024.937l7.93 3.18a2 2 0 0 1 1.112 1.11z M21.854 2.147l-10.94 10.939");

    public static Geometry ServerOff => field ??= Parse("M7 2h13a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2h-5 M10 10 2.5 2.5C2 2 2 2.5 2 5v3a2 2 0 0 0 2 2h6z M22 17v-1a2 2 0 0 0-2-2h-1 M4 14a2 2 0 0 0-2 2v4a2 2 0 0 0 2 2h16.5l1-.5.5.5-8-8H4z M6 18h.01 M2 2l20 20");

    public static Geometry Settings => field ??= Parse("M9.671 4.136a2.34 2.34 0 0 1 4.659 0 2.34 2.34 0 0 0 3.319 1.915 2.34 2.34 0 0 1 2.33 4.033 2.34 2.34 0 0 0 0 3.831 2.34 2.34 0 0 1-2.33 4.033 2.34 2.34 0 0 0-3.319 1.915 2.34 2.34 0 0 1-4.659 0 2.34 2.34 0 0 0-3.32-1.915 2.34 2.34 0 0 1-2.33-4.033 2.34 2.34 0 0 0 0-3.831A2.34 2.34 0 0 1 6.35 6.051a2.34 2.34 0 0 0 3.319-1.915 M9 12a3 3 0 1 0 6 0a3 3 0 1 0 -6 0z");

    public static Geometry ShieldAlert => field ??= Parse("M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z M12 8v4 M12 16h.01");

    public static Geometry ShieldCheck => field ??= Parse("M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z M9 12l2 2 4-4");

    public static Geometry Sparkle => field ??= Parse("M9.937 15.5A2 2 0 0 0 8.5 14.063l-6.135-1.582a.5.5 0 0 1 0-.962L8.5 9.936A2 2 0 0 0 9.937 8.5l1.582-6.135a.5.5 0 0 1 .963 0L14.063 8.5A2 2 0 0 0 15.5 9.937l6.135 1.581a.5.5 0 0 1 0 .964L15.5 14.063a2 2 0 0 0-1.437 1.437l-1.582 6.135a.5.5 0 0 1-.963 0z");

    public static Geometry Sparkles => field ??= Parse("M11.017 2.814a1 1 0 0 1 1.966 0l1.051 5.558a2 2 0 0 0 1.594 1.594l5.558 1.051a1 1 0 0 1 0 1.966l-5.558 1.051a2 2 0 0 0-1.594 1.594l-1.051 5.558a1 1 0 0 1-1.966 0l-1.051-5.558a2 2 0 0 0-1.594-1.594l-5.558-1.051a1 1 0 0 1 0-1.966l5.558-1.051a2 2 0 0 0 1.594-1.594z M20 2v4 M22 4h-4 M2 20a2 2 0 1 0 4 0a2 2 0 1 0 -4 0z");

    public static Geometry Square => field ??= Parse("M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z");

    public static Geometry Store => field ??= Parse("M15 21v-5a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v5 M17.774 10.31a1.12 1.12 0 0 0-1.549 0 2.5 2.5 0 0 1-3.451 0 1.12 1.12 0 0 0-1.548 0 2.5 2.5 0 0 1-3.452 0 1.12 1.12 0 0 0-1.549 0 2.5 2.5 0 0 1-3.77-3.248l2.889-4.184A2 2 0 0 1 7 2h10a2 2 0 0 1 1.653.873l2.895 4.192a2.5 2.5 0 0 1-3.774 3.244 M4 10.95V19a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-8.05");

    public static Geometry Sunrise => field ??= Parse("M12 2v8 M4.93 10.93l1.41 1.41 M2 18h2 M20 18h2 M19.07 10.93l-1.41 1.41 M22 22H2 M8 6l4-4 4 4 M16 18a4 4 0 0 0-8 0");

    public static Geometry Trash => field ??= Parse("M10 11v6 M14 11v6 M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6 M3 6h18 M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2");

    public static Geometry TriangleAlert => field ??= Parse("M21.73 18l-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3 M12 9v4 M12 17h.01");

    public static Geometry Undo => field ??= Parse("M9 14 4 9l5-5 M4 9h10.5a5.5 5.5 0 0 1 5.5 5.5a5.5 5.5 0 0 1-5.5 5.5H11");

    public static Geometry Unlink => field ??= Parse("M18.84 12.25l1.72-1.71h-.02a5.004 5.004 0 0 0-.12-7.07 5.006 5.006 0 0 0-6.95 0l-1.72 1.71 M5.17 11.75l-1.71 1.71a5.004 5.004 0 0 0 .12 7.07 5.006 5.006 0 0 0 6.95 0l1.71-1.71 M8 2v3 M2 8h3 M16 22v-3 M22 16h-3");

    public static Geometry Unplug => field ??= Parse("M19 5l3-3 M2 22l3-3 M6.3 20.3a2.4 2.4 0 0 0 3.4 0L12 18l-6-6-2.3 2.3a2.4 2.4 0 0 0 0 3.4Z M7.5 13.5 10 11 M10.5 16.5 13 14 M12 6l6 6 2.3-2.3a2.4 2.4 0 0 0 0-3.4l-2.6-2.6a2.4 2.4 0 0 0-3.4 0Z");

    public static Geometry Users => field ??= Parse("M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M16 3.128a4 4 0 0 1 0 7.744 M22 21v-2a4 4 0 0 0-3-3.87 M5 7a4 4 0 1 0 8 0a4 4 0 1 0 -8 0z");

    public static Geometry Volume => field ??= Parse("M11 4.702a.705.705 0 0 0-1.203-.498L6.413 7.587A1.4 1.4 0 0 1 5.416 8H3a1 1 0 0 0-1 1v6a1 1 0 0 0 1 1h2.416a1.4 1.4 0 0 1 .997.413l3.383 3.384A.705.705 0 0 0 11 19.298z M16 9a5 5 0 0 1 0 6 M19.364 18.364a9 9 0 0 0 0-12.728");

    public static Geometry WandSparkles => field ??= Parse("M21.64 3.64l-1.28-1.28a1.21 1.21 0 0 0-1.72 0L2.36 18.64a1.21 1.21 0 0 0 0 1.72l1.28 1.28a1.2 1.2 0 0 0 1.72 0L21.64 5.36a1.2 1.2 0 0 0 0-1.72 M14 7l3 3 M5 6v4 M19 14v4 M10 2v2 M7 8H3 M21 16h-4 M11 3H9");

    public static Geometry Wifi => field ??= Parse("M12 20h.01 M2 8.82a15 15 0 0 1 20 0 M5 12.859a10 10 0 0 1 14 0 M8.5 16.429a5 5 0 0 1 7 0");

    public static Geometry WifiOff => field ??= Parse("M12 20h.01 M8.5 16.429a5 5 0 0 1 7 0 M5 12.859a10 10 0 0 1 5.17-2.69 M19 12.859a10 10 0 0 0-2.007-1.523 M2 8.82a15 15 0 0 1 4.177-2.643 M22 8.82a15 15 0 0 0-11.288-3.764 M2 2l20 20");

    public static Geometry Wrench => field ??= Parse("M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z");

    public static Geometry X => field ??= Parse("M18 6 6 18 M6 6l12 12");

    public static Geometry Zap => field ??= Parse("M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z");

    private static Geometry Parse(string data) => Geometry.Parse(data);
}
