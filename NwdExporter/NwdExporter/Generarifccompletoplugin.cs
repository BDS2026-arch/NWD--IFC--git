using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Interop.ComApi;
using Autodesk.Navisworks.Api.Plugins;
using ComApiBridge = Autodesk.Navisworks.Api.ComApi;

namespace NwdExporter
{
    public static class Configuracion
    {
        private static readonly string RutaArchivoConfiguracion = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NwdExporter", "ruta_exe.txt");

        // Devuelve la ruta guardada del .exe, o null si nunca se ha configurado
        // o el archivo guardado ya no existe (por ejemplo, se movió de carpeta).
        public static string ObtenerRutaEjecutableGuardada()
        {
            try
            {
                if (File.Exists(RutaArchivoConfiguracion))
                {
                    string ruta = File.ReadAllText(RutaArchivoConfiguracion).Trim();
                    if (File.Exists(ruta)) return ruta;
                }
            }
            catch { }
            return null;
        }

        public static void GuardarRutaEjecutable(string ruta)
        {
            try
            {
                string carpeta = Path.GetDirectoryName(RutaArchivoConfiguracion);
                if (!Directory.Exists(carpeta)) Directory.CreateDirectory(carpeta);
                File.WriteAllText(RutaArchivoConfiguracion, ruta);
            }
            catch { }
        }

        // Pide la ruta al usuario (con ventana de "elegir archivo") y la guarda para la próxima vez.
        public static string PedirYGuardarRutaEjecutable()
        {
            using (var dialogo = new OpenFileDialog
            {
                Title = "Selecciona construir_ifc.exe",
                Filter = "Ejecutable (*.exe)|*.exe",
            })
            {
                if (dialogo.ShowDialog() != DialogResult.OK)
                    return null;

                GuardarRutaEjecutable(dialogo.FileName);
                return dialogo.FileName;
            }
        }
    }

    // Ventana simple con opciones antes de exportar, para controlar el peso final.
    // Ventana simple de progreso con barra y etiqueta, actualizable desde afuera.
    public class VentanaProgreso : Form
    {
        private ProgressBar barra;
        private Label etiqueta;

        public VentanaProgreso(string titulo)
        {
            Text = titulo;
            Width = 420;
            Height = 130;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;

            etiqueta = new Label { Text = "Iniciando...", Left = 15, Top = 15, Width = 380, AutoSize = false, Height = 20 };
            barra = new ProgressBar { Left = 15, Top = 45, Width = 380, Height = 25, Minimum = 0, Maximum = 100, Value = 0 };

            Controls.Add(etiqueta);
            Controls.Add(barra);
        }

        public void Actualizar(int hecho, int total, string textoExtra = null)
        {
            if (InvokeRequired)
            {
                try { Invoke(new Action(() => Actualizar(hecho, total, textoExtra))); } catch { }
                return;
            }

            int maximo = Math.Max(total, 1);
            barra.Maximum = maximo;
            barra.Value = Math.Min(Math.Max(hecho, 0), maximo);
            etiqueta.Text = textoExtra ?? $"Procesando {hecho} de {total}...";
        }

        public void PonerIndeterminada(string texto)
        {
            if (InvokeRequired)
            {
                try { Invoke(new Action(() => PonerIndeterminada(texto))); } catch { }
                return;
            }
            barra.Style = ProgressBarStyle.Marquee;
            etiqueta.Text = texto;
        }
    }

    public class VentanaOpciones : Form
    {
        public bool IncluirColores { get; private set; } = true;
        public bool IncluirGeometria { get; private set; } = true;
        public bool IncluirPropiedades { get; private set; } = true;
        public bool BuscarEnPadre { get; private set; } = false;
        public bool ModoRevitIfc { get; private set; } = false;
        public double ToleranciaMm { get; private set; } = 0.5;

        private CheckBox chkColores;
        private CheckBox chkGeometria;
        private CheckBox chkPropiedades;
        private CheckBox chkBuscarEnPadre;
        private CheckBox chkModoRevitIfc;
        private TrackBar sliderDetalle;
        private Label lblDetalle;

        public VentanaOpciones()
        {
            Text = "Opciones de exportación";
            Width = 420;
            Height = 430;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var lbl = new Label
            {
                Text = "Elige qué incluir (menos opciones = archivo más liviano):",
                AutoSize = true,
                Left = 15,
                Top = 15,
            };

            chkGeometria = new CheckBox
            {
                Text = "Incluir geometría real (formas 3D). Si lo desmarcas, solo quedan propiedades, sin nada visible.",
                Left = 15,
                Top = 45,
                Width = 380,
                Height = 40,
                Checked = true,
            };

            chkColores = new CheckBox
            {
                Text = "Incluir colores y transparencia",
                Left = 15,
                Top = 90,
                Width = 380,
                Checked = true,
            };

            chkPropiedades = new CheckBox
            {
                Text = "Incluir propiedades (fichas de datos, incluyendo IWP). Si lo desmarcas, las piezas quedan sin ninguna propiedad adjunta.",
                Left = 15,
                Top = 120,
                Width = 380,
                Height = 40,
                Checked = true,
            };

            chkBuscarEnPadre = new CheckBox
            {
                Text = "Buscar propiedades también en el nodo padre (necesario para modelos tipo SmartPlant3D; hace la exportación de propiedades bastante más lenta).",
                Left = 15,
                Top = 162,
                Width = 380,
                Height = 45,
                Checked = false,
            };

            chkModoRevitIfc = new CheckBox
            {
                Text = "Modelo Revit/IFC: buscar el GUID de IFC en un nodo ancestro (útil cuando el IFC original ya tenía todo, y solo agregaste propiedades nuevas en Navisworks).",
                Left = 15,
                Top = 210,
                Width = 380,
                Height = 45,
                Checked = false,
            };

            lblDetalle = new Label
            {
                Text = "Tolerancia de simplificación: 0.5mm (mínima)",
                AutoSize = true,
                Left = 15,
                Top = 262,
            };

            sliderDetalle = new TrackBar
            {
                Left = 15,
                Top = 284,
                Width = 380,
                Minimum = 0,   // representa 0.5mm
                Maximum = 20,  // representa 20mm
                Value = 0,
                TickFrequency = 2,
            };
            sliderDetalle.ValueChanged += (s, e) =>
            {
                double mm = sliderDetalle.Value == 0 ? 0.5 : sliderDetalle.Value;
                lblDetalle.Text = $"Tolerancia de simplificación: {mm}mm" +
                    (mm <= 0.5 ? " (mínima, casi sin efecto en el peso)" : " (más grande = archivo más liviano, menos detalle fino)");
            };

            var btnOk = new Button
            {
                Text = "Continuar",
                Left = 220,
                Top = 350,
                Width = 90,
                DialogResult = DialogResult.OK,
            };

            var btnCancelar = new Button
            {
                Text = "Cancelar",
                Left = 315,
                Top = 350,
                Width = 80,
                DialogResult = DialogResult.Cancel,
            };

            btnOk.Click += (s, e) =>
            {
                IncluirColores = chkColores.Checked;
                IncluirGeometria = chkGeometria.Checked;
                IncluirPropiedades = chkPropiedades.Checked;
                BuscarEnPadre = chkBuscarEnPadre.Checked;
                ModoRevitIfc = chkModoRevitIfc.Checked;
                ToleranciaMm = sliderDetalle.Value == 0 ? 0.5 : sliderDetalle.Value;
            };

            Controls.Add(lbl);
            Controls.Add(chkGeometria);
            Controls.Add(chkColores);
            Controls.Add(chkPropiedades);
            Controls.Add(chkBuscarEnPadre);
            Controls.Add(chkModoRevitIfc);
            Controls.Add(lblDetalle);
            Controls.Add(sliderDetalle);
            Controls.Add(btnOk);
            Controls.Add(btnCancelar);
            AcceptButton = btnOk;
            CancelButton = btnCancelar;
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public class CapturadorTriangulosTodo : InwSimplePrimitivesCB
    {
        // Cada triángulo: 9 valores de posición (p1xyz,p2xyz,p3xyz) + 9 valores de normal (n1xyz,n2xyz,n3xyz)
        public List<double[]> Triangulos = new List<double[]>();
        public double[] Matriz = null;

        public void Line(InwSimpleVertex v1, InwSimpleVertex v2) { }
        public void Point(InwSimpleVertex v1) { }
        public void SnapPoint(InwSimpleVertex v1) { }

        private void AplanarValores(object o, List<double> destino)
        {
            if (o is System.Collections.IEnumerable enumerable && !(o is string))
            {
                foreach (object item in enumerable) AplanarValores(item, destino);
            }
            else
            {
                destino.Add(Convert.ToDouble(o));
            }
        }

        private double[] LeerCoord(object coordObj)
        {
            var valores = new List<double>();
            AplanarValores(coordObj, valores);
            double x = valores[0], y = valores[1], z = valores[2];

            if (Matriz != null && Matriz.Length == 16)
            {
                double m = Matriz[0] * x + Matriz[4] * y + Matriz[8] * z + Matriz[12];
                double n = Matriz[1] * x + Matriz[5] * y + Matriz[9] * z + Matriz[13];
                double o2 = Matriz[2] * x + Matriz[6] * y + Matriz[10] * z + Matriz[14];
                return new double[] { m, n, o2 };
            }
            return new double[] { x, y, z };
        }

        private double[] LeerNormal(object normalObj)
        {
            var valores = new List<double>();
            AplanarValores(normalObj, valores);
            double x = valores[0], y = valores[1], z = valores[2];

            double nx = x, ny = y, nz = z;
            if (Matriz != null && Matriz.Length == 16)
            {
                nx = Matriz[0] * x + Matriz[4] * y + Matriz[8] * z;
                ny = Matriz[1] * x + Matriz[5] * y + Matriz[9] * z;
                nz = Matriz[2] * x + Matriz[6] * y + Matriz[10] * z;
            }

            double largo = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (largo > 0.0000001) { nx /= largo; ny /= largo; nz /= largo; }
            return new double[] { nx, ny, nz };
        }

        public void Triangle(InwSimpleVertex v1, InwSimpleVertex v2, InwSimpleVertex v3)
        {
            try
            {
                object c1raw = v1.coord, c2raw = v2.coord, c3raw = v3.coord;
                object n1raw = v1.normal, n2raw = v2.normal, n3raw = v3.normal;

                double[] p1 = LeerCoord(c1raw);
                double[] p2 = LeerCoord(c2raw);
                double[] p3 = LeerCoord(c3raw);

                double[] n1 = LeerNormal(n1raw);
                double[] n2 = LeerNormal(n2raw);
                double[] n3 = LeerNormal(n3raw);

                Triangulos.Add(new double[]
                {
                    p1[0], p1[1], p1[2], p2[0], p2[1], p2[2], p3[0], p3[1], p3[2],
                    n1[0], n1[1], n1[2], n2[0], n2[1], n2[2], n3[0], n3[1], n3[2],
                });
            }
            catch
            {
                // Triángulo problemático: se salta, no debe tumbar el proceso completo.
            }
        }
    }

    [Plugin("GenerarIfcCompleto", "MICO", DisplayName = "Generar IFC completo",
        ToolTip = "Exporta propiedades + geometría y genera el IFC final automáticamente")]
    [AddInPlugin(AddInLocation.AddIn)]
    public class GenerarIfcCompletoPlugin : AddInPlugin
    {
        private int _contadorId = 0;
        private int _verticeGlobal = 0;
        private int _diagPathNulo = 0;
        private int _diagSinFragmentos = 0;
        private int _diagErrorFragmento = 0;
        private int _diagConTriangulos = 0;
        private int _diagSinTriangulos = 0;
        private string _diagPrimerError = "";
        private long _tiempoPropiedadesTicks = 0;
        private long _tiempoGeometriaTicks = 0;
        private long _nodosVisitadosTotal = 0;
        private long _tiempoChildrenTicks = 0;
        private long _tiempoBboxTicks = 0;
        private long _tiempoEnumTicks = 0;
        private long _tiempoCategoriasTicks = 0;
        private bool _incluirColores = true;
        private bool _incluirGeometria = true;
        private bool _incluirPropiedades = true;
        private bool _buscarEnPadre = false;
        private bool _modoRevitIfc = false;
        private double _toleranciaMm = 0.5;

        public override int Execute(params string[] parameters)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            try
            {
                Document doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
                if (doc == null)
                {
                    MessageBox.Show("No hay ningún documento abierto en Navisworks.");
                    return 1;
                }

                string rutaEjecutable = Configuracion.ObtenerRutaEjecutableGuardada();
                if (rutaEjecutable == null)
                {
                    MessageBox.Show("No se encontró construir_ifc.exe en la ubicación guardada (o es la primera vez que se usa).\n" +
                        "Selecciona dónde está el archivo -- se recuerda automáticamente para la próxima vez.");
                    rutaEjecutable = Configuracion.PedirYGuardarRutaEjecutable();
                    if (rutaEjecutable == null)
                    {
                        MessageBox.Show("No se seleccionó ningún archivo. Operación cancelada.");
                        return 1;
                    }
                }

                string rutaIfcOriginal = null;

                using (var ventanaOpciones = new VentanaOpciones())
                {
                    if (ventanaOpciones.ShowDialog() != DialogResult.OK)
                        return 1;

                    _incluirColores = ventanaOpciones.IncluirColores;
                    _incluirGeometria = ventanaOpciones.IncluirGeometria;
                    _incluirPropiedades = ventanaOpciones.IncluirPropiedades;
                    _buscarEnPadre = ventanaOpciones.BuscarEnPadre;
                    _modoRevitIfc = ventanaOpciones.ModoRevitIfc;
                    _toleranciaMm = ventanaOpciones.ToleranciaMm;

                    if (_modoRevitIfc)
                    {
                        // En este modo no hace falta geometría: ya está perfecta en el IFC original.
                        _incluirGeometria = false;

                        using (var dialogoIfcOriginal = new OpenFileDialog
                        {
                            Title = "Selecciona el IFC ORIGINAL (el que exportó Revit)",
                            Filter = "IFC (*.ifc)|*.ifc",
                        })
                        {
                            if (dialogoIfcOriginal.ShowDialog() != DialogResult.OK)
                            {
                                MessageBox.Show("No se seleccionó el IFC original. Operación cancelada.");
                                return 1;
                            }
                            rutaIfcOriginal = dialogoIfcOriginal.FileName;
                        }
                    }
                }

                using (var dialogoCarpeta = new FolderBrowserDialog { Description = "Elige la carpeta donde guardar todo" })
                {
                    if (dialogoCarpeta.ShowDialog() != DialogResult.OK)
                        return 1;

                    string carpeta = dialogoCarpeta.SelectedPath;

                    string nombreBase = "modelo";
                    try
                    {
                        string rutaOriginal = doc.CurrentFileName;
                        if (!string.IsNullOrWhiteSpace(rutaOriginal))
                            nombreBase = Path.GetFileNameWithoutExtension(rutaOriginal);
                    }
                    catch { }

                    string sufijo;
                    if (_incluirGeometria && _incluirColores && _incluirPropiedades) sufijo = "_completo";
                    else if (_incluirGeometria && _incluirColores && !_incluirPropiedades) sufijo = "_geometriaycolor";
                    else if (_incluirGeometria && !_incluirColores && _incluirPropiedades) sufijo = "_geometriaydatos";
                    else if (_incluirGeometria && !_incluirColores && !_incluirPropiedades) sufijo = "_geometria";
                    else if (!_incluirGeometria && _incluirPropiedades) sufijo = "_datos";
                    else sufijo = "_export";

                    string rutaCsv = Path.Combine(carpeta, nombreBase + sufijo + "_propiedades.csv");
                    string rutaObj = Path.Combine(carpeta, nombreBase + sufijo + "_geometria.obj");
                    string rutaIfc = Path.Combine(carpeta, nombreBase + sufijo + ".ifc");

                    _contadorId = 0;
                    _verticeGlobal = 0;
                    int elementosConProps = 0;

                    bool usandoSeleccion = doc.CurrentSelection.SelectedItems.Count > 0;
                    IEnumerable<ModelItem> raices;
                    if (usandoSeleccion)
                        raices = doc.CurrentSelection.SelectedItems;
                    else
                        raices = doc.Models.RootItems;

                    var ventanaProgresoNavis = new VentanaProgreso("Exportando desde Navisworks...");
                    ventanaProgresoNavis.PonerIndeterminada("Procesando elementos (esto puede tardar varios minutos)...");
                    ventanaProgresoNavis.Show();
                    System.Windows.Forms.Application.DoEvents();

                    _procesadosNavis = 0;
                    _totalEstimadoNavis = 0; // sin conteo previo: no sabemos el total de antemano
                    _ventanaProgresoNavis = ventanaProgresoNavis;

                    var cronoTotal = Stopwatch.StartNew();
                    using (StreamWriter writerCsv = new StreamWriter(rutaCsv, false, System.Text.Encoding.UTF8))
                    using (StreamWriter writerObj = new StreamWriter(rutaObj, false))
                    {
                        writerCsv.WriteLine("Id,Nombre,MinX,MinY,MinZ,MaxX,MaxY,MaxZ,Categoria,Propiedad,Valor,GuidIfc");

                        foreach (ModelItem raiz in raices)
                        {
                            elementosConProps += Recorrer(raiz, writerCsv, writerObj);
                        }
                    }
                    cronoTotal.Stop();

                    ventanaProgresoNavis.Close();

                    MessageBox.Show(
                        $"Exportación desde Navisworks lista ({(usandoSeleccion ? "solo selección" : "modelo completo")}).\n" +
                        $"{elementosConProps} elementos procesados.\n\n" +
                        $"--- Diagnóstico de geometría ---\n" +
                        $"Sin path COM: {_diagPathNulo}\n" +
                        $"Sin fragmentos: {_diagSinFragmentos}\n" +
                        $"Error en fragmento: {_diagErrorFragmento}\n" +
                        $"Con fragmentos pero 0 triángulos: {_diagSinTriangulos}\n" +
                        $"Con triángulos: {_diagConTriangulos}\n" +
                        (string.IsNullOrEmpty(_diagPrimerError) ? "" : $"Primer error: {_diagPrimerError}\n") +
                        $"\n--- Tiempo ---\n" +
                        $"TOTAL real (recorrido completo): {cronoTotal.ElapsedMilliseconds / 1000.0:F1}s\n" +
                        $"  Propiedades (solo hojas): {(double)_tiempoPropiedadesTicks / Stopwatch.Frequency:F2}s\n" +
                        $"  Geometría (solo hojas): {(double)_tiempoGeometriaTicks / Stopwatch.Frequency:F2}s\n" +
                        $"Nodos totales visitados (hojas + carpetas/grupos): {_nodosVisitadosTotal}\n" +
                        $"  Tiempo en revisar item.Children: {(double)_tiempoChildrenTicks / Stopwatch.Frequency:F2}s\n" +
                        $"  Tiempo en item.BoundingBox(): {(double)_tiempoBboxTicks / Stopwatch.Frequency:F2}s\n" +
                        $"  Tiempo en avanzar por la lista de hijos (sin contar el trabajo de cada uno): {(double)_tiempoEnumTicks / Stopwatch.Frequency:F2}s\n" +
                        $"  Tiempo en CONSULTAR categorías (elemento+padre, en TODOS los nodos): {(double)_tiempoCategoriasTicks / Stopwatch.Frequency:F2}s\n" +
                        "\nAhora se va a generar el IFC final (puede tardar varios minutos).");

                    var ventanaProgresoIfc = new VentanaProgreso(_modoRevitIfc ? "Fusionando propiedades en el IFC original..." : "Generando IFC...");
                    ventanaProgresoIfc.Actualizar(0, elementosConProps, $"Procesando 0 de {elementosConProps} elementos...");
                    ventanaProgresoIfc.Show();
                    System.Windows.Forms.Application.DoEvents();

                    bool exito;
                    string salidaPython;
                    if (_modoRevitIfc)
                    {
                        exito = EjecutarPythonFusion(rutaEjecutable, rutaIfcOriginal, rutaCsv, rutaIfc, ventanaProgresoIfc, out salidaPython);
                    }
                    else
                    {
                        exito = EjecutarPython(rutaEjecutable, rutaCsv, rutaIfc, rutaObj, _toleranciaMm, elementosConProps, ventanaProgresoIfc, out salidaPython);
                    }

                    ventanaProgresoIfc.Close();

                    if (exito)
                    {
                        MessageBox.Show($"¡Listo! IFC generado en:\n{rutaIfc}\n\n--- Salida ---\n{salidaPython}");
                    }
                    else
                    {
                        MessageBox.Show($"El paso de construcción del IFC falló o no se pudo ejecutar.\n\n--- Detalle ---\n{salidaPython}\n\n" +
                            $"Puedes correrlo tú mismo manualmente:\n\"{rutaEjecutable}\" \"{rutaCsv}\" \"{rutaIfc}\" \"{rutaObj}\"");
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ocurrió un error inesperado:\n\n" + ex.ToString());
                return 1;
            }
        }

        private int ContarElementos(ModelItem item)
        {
            int total = 0;
            try
            {
                bool tieneAlgunaPropiedad = false;
                foreach (PropertyCategory categoria in item.PropertyCategories)
                {
                    if (categoria.Properties.Count > 0) { tieneAlgunaPropiedad = true; break; }
                }
                bool esHoja = true;
                foreach (ModelItem hijoCheck in item.Children) { esHoja = false; break; }
                if (tieneAlgunaPropiedad && esHoja) total = 1;
            }
            catch { }

            try
            {
                foreach (ModelItem hijo in item.Children) total += ContarElementos(hijo);
            }
            catch { }

            return total;
        }

        private int _procesadosNavis = 0;
        private int _totalEstimadoNavis = 0;
        private VentanaProgreso _ventanaProgresoNavis = null;

        private bool EjecutarPython(string rutaEjecutable, string rutaCsv, string rutaIfc, string rutaObj, double toleranciaMm, int totalElementos, VentanaProgreso ventanaProgreso, out string salida)
        {
            try
            {
                if (!File.Exists(rutaEjecutable))
                {
                    salida = $"No se encontró el ejecutable en:\n{rutaEjecutable}\n\n" +
                        "Es posible que se haya movido o borrado. Vuelve a correr el plugin para elegir la ubicación de nuevo.";
                    return false;
                }

                string toleranciaTexto = toleranciaMm.ToString("F2", CultureInfo.InvariantCulture);
                // Los modelos tipo SmartPlant3D (casilla "Buscar en el padre" activada) suelen
                // tener coordenadas muy grandes que dan problemas de precisión al hacer zoom en
                // los visores -- para esos casos sí aplicamos el desplazamiento. Para los demás
                // (Plant3D/Civil3D normales) se mantienen las coordenadas originales.
                string usarOffsetTexto = _buscarEnPadre ? "1" : "0";

                var psi = new ProcessStartInfo
                {
                    FileName = rutaEjecutable,
                    Arguments = $"\"{rutaCsv}\" \"{rutaIfc}\" \"{rutaObj}\" 0 {toleranciaTexto} {usarOffsetTexto}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

                var salidaCompleta = new System.Text.StringBuilder();
                var regexProgreso = new System.Text.RegularExpressions.Regex(@"\.\.\.(\d+) elementos procesados");

                using (var proceso = new Process { StartInfo = psi, EnableRaisingEvents = true })
                {
                    proceso.OutputDataReceived += (s, e) =>
                    {
                        if (string.IsNullOrEmpty(e.Data)) return;
                        salidaCompleta.AppendLine(e.Data);

                        var m = regexProgreso.Match(e.Data);
                        if (m.Success && int.TryParse(m.Groups[1].Value, out int procesados))
                        {
                            ventanaProgreso?.Actualizar(procesados, totalElementos,
                                $"Generando IFC: {procesados} de {totalElementos} elementos...");
                        }
                        else if (ventanaProgreso != null)
                        {
                            ventanaProgreso.PonerIndeterminada(e.Data.Length > 60 ? e.Data.Substring(0, 60) + "..." : e.Data);
                        }
                    };
                    proceso.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data)) salidaCompleta.AppendLine("[ERROR] " + e.Data);
                    };

                    proceso.Start();
                    proceso.BeginOutputReadLine();
                    proceso.BeginErrorReadLine();

                    while (!proceso.HasExited)
                    {
                        proceso.WaitForExit(100);
                        System.Windows.Forms.Application.DoEvents();
                    }

                    salida = salidaCompleta.ToString();
                    return proceso.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                salida = "No se pudo iniciar Python: " + ex.Message;
                return false;
            }
        }

        // Igual que EjecutarPython, pero para el modo Revit/IFC: llama al mismo
        // .exe con la bandera --fusionar, para inyectar propiedades en el IFC
        // original en vez de construir uno desde cero.
        private bool EjecutarPythonFusion(string rutaEjecutable, string rutaIfcOriginal, string rutaCsv, string rutaIfcSalida, VentanaProgreso ventanaProgreso, out string salida)
        {
            try
            {
                if (!File.Exists(rutaEjecutable))
                {
                    salida = $"No se encontró el ejecutable en:\n{rutaEjecutable}\n\n" +
                        "Es posible que se haya movido o borrado. Vuelve a correr el plugin para elegir la ubicación de nuevo.";
                    return false;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = rutaEjecutable,
                    Arguments = $"--fusionar \"{rutaIfcOriginal}\" \"{rutaCsv}\" \"{rutaIfcSalida}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

                var salidaCompleta = new System.Text.StringBuilder();
                var regexProgreso = new System.Text.RegularExpressions.Regex(@"\.\.\.(\d+) de (\d+) procesados");

                using (var proceso = new Process { StartInfo = psi, EnableRaisingEvents = true })
                {
                    proceso.OutputDataReceived += (s, e) =>
                    {
                        if (string.IsNullOrEmpty(e.Data)) return;
                        salidaCompleta.AppendLine(e.Data);

                        var m = regexProgreso.Match(e.Data);
                        if (m.Success && int.TryParse(m.Groups[1].Value, out int procesados) && int.TryParse(m.Groups[2].Value, out int totalFusion))
                        {
                            ventanaProgreso?.Actualizar(procesados, totalFusion,
                                $"Fusionando: {procesados} de {totalFusion} elementos...");
                        }
                        else if (ventanaProgreso != null)
                        {
                            ventanaProgreso.PonerIndeterminada(e.Data.Length > 60 ? e.Data.Substring(0, 60) + "..." : e.Data);
                        }
                    };
                    proceso.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data)) salidaCompleta.AppendLine("[ERROR] " + e.Data);
                    };

                    proceso.Start();
                    proceso.BeginOutputReadLine();
                    proceso.BeginErrorReadLine();

                    while (!proceso.HasExited)
                    {
                        proceso.WaitForExit(100);
                        System.Windows.Forms.Application.DoEvents();
                    }

                    salida = salidaCompleta.ToString();
                    return proceso.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                salida = "No se pudo iniciar Python: " + ex.Message;
                return false;
            }
        }

        // Color y transparencia vía la API .NET normal (más confiable que la ruta COM por vértice).
        private double[] LeerColorYTransparencia(ModelItem item)
        {
            try
            {
                var geometria = item.FindFirstGeometry();
                if (geometria == null) return null;

                Autodesk.Navisworks.Api.Color color = geometria.ActiveColor;
                double transparencia = 0.0;
                try { transparencia = geometria.ActiveTransparency; } catch { transparencia = 0.0; }

                double alfa = 1.0 - transparencia; // Navisworks: 0=opaco, 1=invisible. Nosotros usamos alfa (1=opaco).
                return new double[] { color.R, color.G, color.B, alfa };
            }
            catch
            {
                return null;
            }
        }

        // Categorías propias del elemento + las de su padre INMEDIATO (una sola
        // vez por padre, gracias a la caché) -- no toca abuelos ni niveles más
        // arriba, así que es mucho más rápido que acumular por todo el árbol.
        private Dictionary<ModelItem, List<Tuple<string, PropertyCategory>>> _cachePadres =
            new Dictionary<ModelItem, List<Tuple<string, PropertyCategory>>>();

        private List<Tuple<string, PropertyCategory>> CategoriasPropiasDe(ModelItem item)
        {
            var resultado = new List<Tuple<string, PropertyCategory>>();
            try
            {
                foreach (PropertyCategory categoria in item.PropertyCategories)
                {
                    if (categoria.Properties.Count == 0) continue;
                    resultado.Add(Tuple.Create(Limpiar(categoria.DisplayName), categoria));
                }
            }
            catch { }
            return resultado;
        }

        // Sube por los ancestros (padre, abuelo, ...) hasta encontrar un nodo que
        // tenga una propiedad "IfcGUID" o "GlobalId" (típicamente en la ficha
        // "IFC Type" de modelos que vienen originalmente de un IFC de Revit
        // cargado en Navisworks). Devuelve el GUID Y, además, todas las demás
        // categorías de propiedades que haya en ESE MISMO nodo ancestro (por si
        // agregaste propiedades nuevas ahí, no solo en la geometría).
        private Dictionary<ModelItem, Tuple<string, List<Tuple<string, PropertyCategory>>>> _cacheAncestroIfc =
            new Dictionary<ModelItem, Tuple<string, List<Tuple<string, PropertyCategory>>>>();

        private Tuple<string, List<Tuple<string, PropertyCategory>>> BuscarGuidYPropiedadesEnAncestro(ModelItem item)
        {
            ModelItem actual = item;
            var visitados = new List<ModelItem>();
            int niveles = 0;

            while (actual != null && niveles < 20)
            {
                if (_cacheAncestroIfc.TryGetValue(actual, out var cacheado))
                {
                    foreach (var v in visitados) _cacheAncestroIfc[v] = cacheado;
                    return cacheado;
                }

                visitados.Add(actual);

                string guidEncontrado = null;
                try
                {
                    foreach (PropertyCategory categoria in actual.PropertyCategories)
                    {
                        foreach (DataProperty prop in categoria.Properties)
                        {
                            string nombreProp = (prop.DisplayName ?? "").Trim().ToLowerInvariant();
                            if (nombreProp == "ifcguid" || nombreProp == "globalid")
                            {
                                string valor = "";
                                try { valor = prop.Value?.ToDisplayString() ?? ""; } catch { }
                                if (!string.IsNullOrWhiteSpace(valor))
                                {
                                    guidEncontrado = valor;
                                    break;
                                }
                            }
                        }
                        if (guidEncontrado != null) break;
                    }
                }
                catch { }

                if (guidEncontrado != null)
                {
                    // Encontramos el nodo correcto: recolectamos TODAS sus categorías
                    // de propiedades (menos "IFC Type", que ya está en el IFC original).
                    var otrasCategorias = new List<Tuple<string, PropertyCategory>>();
                    try
                    {
                        foreach (PropertyCategory categoria in actual.PropertyCategories)
                        {
                            if (categoria.Properties.Count == 0) continue;
                            string nombreCategoria = Limpiar(categoria.DisplayName);
                            if (nombreCategoria.Trim().ToLowerInvariant() == "ifc type") continue;
                            otrasCategorias.Add(Tuple.Create(nombreCategoria, categoria));
                        }
                    }
                    catch { }

                    var resultado = Tuple.Create(guidEncontrado, otrasCategorias);
                    foreach (var v in visitados) _cacheAncestroIfc[v] = resultado;
                    return resultado;
                }

                try { actual = actual.Parent; } catch { actual = null; }
                niveles++;
            }

            var vacio = Tuple.Create("", new List<Tuple<string, PropertyCategory>>());
            foreach (var v in visitados) _cacheAncestroIfc[v] = vacio;
            return vacio;
        }

        private List<Tuple<string, PropertyCategory>> CategoriasElementoMasPadre(ModelItem item)
        {
            var propias = CategoriasPropiasDe(item);

            ModelItem padre = null;
            try { padre = item.Parent; } catch { }
            if (padre == null) return propias;

            List<Tuple<string, PropertyCategory>> categoriasPadre;
            if (!_cachePadres.TryGetValue(padre, out categoriasPadre))
            {
                categoriasPadre = CategoriasPropiasDe(padre);
                _cachePadres[padre] = categoriasPadre;
            }

            if (categoriasPadre.Count == 0) return propias;

            var nombresVistos = new HashSet<string>(propias.Select(p => p.Item1), StringComparer.OrdinalIgnoreCase);
            var resultado = new List<Tuple<string, PropertyCategory>>(propias);
            foreach (var par in categoriasPadre)
            {
                if (nombresVistos.Contains(par.Item1)) continue;
                nombresVistos.Add(par.Item1);
                resultado.Add(par);
            }
            return resultado;
        }

        // Junta las categorías propias de este nodo (si las tiene) con las heredadas
        // de sus ancestros (ya calculadas antes, no se vuelven a consultar).
        private List<Tuple<string, PropertyCategory>> CombinarConPropias(ModelItem item, List<Tuple<string, PropertyCategory>> heredadas)
        {
            var resultado = new List<Tuple<string, PropertyCategory>>();
            var nombresVistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (PropertyCategory categoria in item.PropertyCategories)
                {
                    if (categoria.Properties.Count == 0) continue;
                    string nombreCategoria = Limpiar(categoria.DisplayName);
                    if (nombresVistos.Contains(nombreCategoria)) continue;
                    nombresVistos.Add(nombreCategoria);
                    resultado.Add(Tuple.Create(nombreCategoria, categoria));
                }
            }
            catch { }

            if (heredadas != null)
            {
                foreach (var par in heredadas)
                {
                    if (nombresVistos.Contains(par.Item1)) continue;
                    nombresVistos.Add(par.Item1);
                    resultado.Add(par);
                }
            }

            return resultado;
        }

        private int Recorrer(ModelItem item, StreamWriter writerCsv, StreamWriter writerObj, List<Tuple<string, PropertyCategory>> heredadas = null)
        {
            int total = 0;
            List<Tuple<string, PropertyCategory>> categoriasAqui = null;
            _nodosVisitadosTotal++;

            ModelItemEnumerableCollection hijos = null;
            var cronoChildren = Stopwatch.StartNew();
            try
            {
                hijos = item.Children;
                bool esHoja = true;
                foreach (ModelItem hijoCheck in hijos) { esHoja = false; break; }
                cronoChildren.Stop();
                _tiempoChildrenTicks += cronoChildren.ElapsedTicks;

                var cronoCategorias = Stopwatch.StartNew();
                if (!esHoja)
                {
                    // En carpetas/grupos no consultamos propiedades para nada
                    // (ni propias ni de padre) -- solo interesa en piezas finales.
                    categoriasAqui = new List<Tuple<string, PropertyCategory>>();
                }
                else if (_buscarEnPadre)
                {
                    // Solo el elemento + su padre INMEDIATO (con caché por padre).
                    categoriasAqui = CategoriasElementoMasPadre(item);
                }
                else
                {
                    categoriasAqui = CategoriasPropiasDe(item);
                }
                cronoCategorias.Stop();
                _tiempoCategoriasTicks += cronoCategorias.ElapsedTicks;

                bool tieneAlgunaPropiedad = categoriasAqui.Count > 0;

                if (tieneAlgunaPropiedad && esHoja)
                {
                    _contadorId++;
                    int id = _contadorId;
                    var cronoBbox = Stopwatch.StartNew();
                    BoundingBox3D bbox = null;
                    try { bbox = item.BoundingBox(); } catch { }
                    cronoBbox.Stop();
                    _tiempoBboxTicks += cronoBbox.ElapsedTicks;
                    string nombre = Limpiar(item.DisplayName);

                    double minX = 0, minY = 0, minZ = 0, maxX = 0, maxY = 0, maxZ = 0;
                    if (bbox != null)
                    {
                        minX = bbox.Min.X; minY = bbox.Min.Y; minZ = bbox.Min.Z;
                        maxX = bbox.Max.X; maxY = bbox.Max.Y; maxZ = bbox.Max.Z;
                    }

                    string guidIfc = "";
                    if (_modoRevitIfc)
                    {
                        var resultadoAncestro = BuscarGuidYPropiedadesEnAncestro(item);
                        guidIfc = resultadoAncestro.Item1;
                    }

                    var cronoProps = Stopwatch.StartNew();
                    if (_incluirPropiedades)
                    {
                        foreach (var par in categoriasAqui)
                        {
                            string nombreCategoria = par.Item1;
                            PropertyCategory categoria = par.Item2;

                            foreach (DataProperty prop in categoria.Properties)
                            {
                                string nombreProp = Limpiar(prop.DisplayName);
                                string valor;
                                try { valor = Limpiar(prop.Value?.ToDisplayString() ?? ""); } catch { valor = ""; }
                                writerCsv.WriteLine($"{id},\"{nombre}\",{minX},{minY},{minZ},{maxX},{maxY},{maxZ},\"{nombreCategoria}\",\"{nombreProp}\",\"{valor}\",\"{guidIfc}\"");
                            }
                        }
                    }
                    else
                    {
                        // Sin propiedades: igual dejamos una fila mínima para que el elemento
                        // exista en el CSV y conserve su posición/geometría.
                        writerCsv.WriteLine($"{id},\"{nombre}\",{minX},{minY},{minZ},{maxX},{maxY},{maxZ},\"\",\"\",\"\",\"{guidIfc}\"");
                    }
                    cronoProps.Stop();
                    _tiempoPropiedadesTicks += cronoProps.ElapsedTicks;

                    var cronoGeom = Stopwatch.StartNew();
                    if (_incluirGeometria)
                    {
                        // Suavizamos (agregamos normales) según el tipo de modelo:
                        //  - Modelos normales (Plant3D/Civil3D): solo si la categoría dice
                        //    "Civil", o si la pieza tiene MUCHOS triángulos (superficie grande
                        //    de terreno) -- así no se dispara el peso en tuberías comunes.
                        //  - Modelos tipo SmartPlant3D (casilla "Buscar en el padre" activada):
                        //    umbral mucho más bajo, porque ahí las tuberías/curvas tienen menos
                        //    triángulos pero igual se ven mal sin suavizar.
                        int umbralTriangulos = _buscarEnPadre ? 3 : 200;

                        bool esCategoriaCivil = categoriasAqui.Any(c =>
                            c.Item1.IndexOf("Civil", StringComparison.OrdinalIgnoreCase) >= 0);

                        List<double[]> triangulos = ObtenerTriangulos(item, true);
                        bool convieneSuavizar = esCategoriaCivil || triangulos.Count >= umbralTriangulos;
                        if (triangulos.Count > 0)
                        {
                            writerObj.WriteLine($"g {id}");

                            if (_incluirColores)
                            {
                                double[] colorMuestra = LeerColorYTransparencia(item);
                                if (colorMuestra != null)
                                {
                                    writerObj.WriteLine($"# color {colorMuestra[0]:F4} {colorMuestra[1]:F4} {colorMuestra[2]:F4} {colorMuestra[3]:F4}");
                                }
                            }

                            if (convieneSuavizar)
                            {
                                var indicePorVertice = new Dictionary<(long, long, long, long, long, long), int>();
                                foreach (double[] tri in triangulos)
                                {
                                    int i1 = ObtenerOAgregarIndiceConNormal(tri[0], tri[1], tri[2], tri[9], tri[10], tri[11], indicePorVertice, writerObj);
                                    int i2 = ObtenerOAgregarIndiceConNormal(tri[3], tri[4], tri[5], tri[12], tri[13], tri[14], indicePorVertice, writerObj);
                                    int i3 = ObtenerOAgregarIndiceConNormal(tri[6], tri[7], tri[8], tri[15], tri[16], tri[17], indicePorVertice, writerObj);
                                    writerObj.WriteLine($"f {i1}//{i1} {i2}//{i2} {i3}//{i3}");
                                }
                            }
                            else
                            {
                                var indicePorVertice = new Dictionary<(long, long, long), int>();
                                foreach (double[] tri in triangulos)
                                {
                                    int i1 = ObtenerOAgregarIndiceSinNormal(tri[0], tri[1], tri[2], indicePorVertice, writerObj);
                                    int i2 = ObtenerOAgregarIndiceSinNormal(tri[3], tri[4], tri[5], indicePorVertice, writerObj);
                                    int i3 = ObtenerOAgregarIndiceSinNormal(tri[6], tri[7], tri[8], indicePorVertice, writerObj);
                                    writerObj.WriteLine($"f {i1} {i2} {i3}");
                                }
                            }
                        }
                    }
                    cronoGeom.Stop();
                    _tiempoGeometriaTicks += cronoGeom.ElapsedTicks;

                    total = 1;
                    _procesadosNavis++;
                    if (_ventanaProgresoNavis != null && _procesadosNavis % 15 == 0)
                    {
                        double segProps = (double)_tiempoPropiedadesTicks / Stopwatch.Frequency;
                        double segGeom = (double)_tiempoGeometriaTicks / Stopwatch.Frequency;
                        _ventanaProgresoNavis.PonerIndeterminada(
                            $"Procesando... {_procesadosNavis} elementos | props: {segProps:F1}s | geometría: {segGeom:F1}s");
                        System.Windows.Forms.Application.DoEvents();
                    }
                }
            }
            catch { }

            try
            {
                if (hijos != null)
                {
                    var cronoEnum = Stopwatch.StartNew();
                    foreach (ModelItem hijo in hijos)
                    {
                        cronoEnum.Stop();
                        _tiempoEnumTicks += cronoEnum.ElapsedTicks;

                        total += Recorrer(hijo, writerCsv, writerObj);

                        cronoEnum.Restart();
                    }
                    cronoEnum.Stop();
                    _tiempoEnumTicks += cronoEnum.ElapsedTicks;
                }
            }
            catch { }

            return total;
        }

        private int ObtenerOAgregarIndiceConNormal(double x, double y, double z, double nx, double ny, double nz,
            Dictionary<(long, long, long, long, long, long), int> mapa, StreamWriter writer)
        {
            var clave = (
                (long)Math.Round(x * 1000.0), (long)Math.Round(y * 1000.0), (long)Math.Round(z * 1000.0),
                (long)Math.Round(nx * 1000.0), (long)Math.Round(ny * 1000.0), (long)Math.Round(nz * 1000.0)
            );
            if (mapa.TryGetValue(clave, out int indiceExistente)) return indiceExistente;

            _verticeGlobal++;
            writer.WriteLine($"v {x:F1} {y:F1} {z:F1}");
            writer.WriteLine($"vn {nx:F4} {ny:F4} {nz:F4}");
            mapa[clave] = _verticeGlobal;
            return _verticeGlobal;
        }

        private int ObtenerOAgregarIndiceSinNormal(double x, double y, double z,
            Dictionary<(long, long, long), int> mapa, StreamWriter writer)
        {
            var clave = ((long)Math.Round(x * 1000.0), (long)Math.Round(y * 1000.0), (long)Math.Round(z * 1000.0));
            if (mapa.TryGetValue(clave, out int indiceExistente)) return indiceExistente;

            _verticeGlobal++;
            writer.WriteLine($"v {x:F1} {y:F1} {z:F1}");
            mapa[clave] = _verticeGlobal;
            return _verticeGlobal;
        }

        private double[] LeerMatrizDelFragmento(InwOaFragment3 frag)
        {
            object transformObj = frag.GetLocalToWorldMatrix();
            if (transformObj == null) return null;
            object matrixObj = transformObj.GetType().InvokeMember("Matrix", BindingFlags.GetProperty, null, transformObj, null);
            Array arr = (Array)matrixObj;
            int inferior = arr.GetLowerBound(0);
            double[] m = new double[16];
            for (int i = 0; i < 16; i++) m[i] = Convert.ToDouble(arr.GetValue(inferior + i));
            return m;
        }

        private List<double[]> ObtenerTriangulos(ModelItem item, bool conNormales)
        {
            var resultado = new List<double[]>();
            InwOaPath oPath;
            try { oPath = ComApiBridge.ComApiBridge.ToInwOaPath(item); }
            catch (Exception ex)
            {
                _diagPathNulo++;
                if (string.IsNullOrEmpty(_diagPrimerError)) _diagPrimerError = "ToInwOaPath(): " + ex.Message;
                return resultado;
            }
            if (oPath == null) { _diagPathNulo++; return resultado; }

            object fragmentosObj;
            try { fragmentosObj = oPath.Fragments(); }
            catch (Exception ex)
            {
                _diagErrorFragmento++;
                if (string.IsNullOrEmpty(_diagPrimerError)) _diagPrimerError = "Fragments(): " + ex.Message;
                return resultado;
            }
            if (fragmentosObj == null) { _diagSinFragmentos++; return resultado; }

            var capturador = new CapturadorTriangulosTodo();
            int contadorFragmentos = 0;
            nwEVertexProperty bandera = conNormales ? nwEVertexProperty.eNORMAL : nwEVertexProperty.eNONE;

            foreach (object oFragObj in (System.Collections.IEnumerable)fragmentosObj)
            {
                contadorFragmentos++;
                try
                {
                    InwOaFragment3 oFrag = oFragObj as InwOaFragment3;
                    if (oFrag == null) continue;
                    try { capturador.Matriz = LeerMatrizDelFragmento(oFrag); } catch { capturador.Matriz = null; }
                    oFrag.GenerateSimplePrimitives(bandera, capturador);
                }
                catch (Exception ex)
                {
                    _diagErrorFragmento++;
                    if (string.IsNullOrEmpty(_diagPrimerError)) _diagPrimerError = "GenerateSimplePrimitives(): " + ex.Message;
                }
            }

            if (contadorFragmentos == 0) _diagSinFragmentos++;
            else if (capturador.Triangulos.Count == 0) _diagSinTriangulos++;
            else _diagConTriangulos++;

            resultado.AddRange(capturador.Triangulos);
            return resultado;
        }

        private string Limpiar(string texto)
        {
            return (texto ?? "").Replace("\"", "'").Replace("\r", " ").Replace("\n", " ");
        }
    }
}