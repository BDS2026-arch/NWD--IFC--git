"""
Construye un IFC a partir de:
  1. El CSV de propiedades exportado por "Exportar propiedades a CSV"
  2. (Opcional) El OBJ de geometría exportado por "Exportar geometría real a OBJ"

Si no le das el archivo OBJ, usa una caja simple por elemento (como antes).
Si sí le das el OBJ, usa la geometría triangular real.

Uso normal (construir un IFC desde cero):
    python construir_ifc.py "propiedades.csv" "salida.ifc"
    python construir_ifc.py "propiedades.csv" "salida.ifc" "geometria.obj"
    python construir_ifc.py "propiedades.csv" "salida.ifc" "geometria.obj" [limite] [tolerancia_mm] [usar_offset]

Uso modo fusión (Revit/IFC -- inyectar propiedades en un IFC ya existente,
emparejando por GUID, sin tocar la geometría original):
    python construir_ifc.py --fusionar "original.ifc" "propiedades.csv" "salida.ifc"

tolerancia_mm: qué tan cerca deben estar dos puntos para considerarse "el mismo"
    al construir la malla (suelda puntos repetidos, sin perder triángulos).
    Por defecto 0.5mm. Valores más grandes (ej. 5, 10, 20) sueldan más agresivo
    y también empiezan a descartar triángulos muy pequeños, reduciendo el peso
    a cambio de algo de detalle fino.
"""

import csv
import sys
from collections import defaultdict

import ifcopenshell
import ifcopenshell.api


def api_flex(usecase, model, **kwargs):
    """
    Ejecuta ifcopenshell.api.run, y si el nombre de un parámetro no es el
    esperado por la versión instalada (ej. 'product' vs 'products'), intenta
    la variante alterna automáticamente antes de fallar.
    """
    try:
        return ifcopenshell.api.run(usecase, model, **kwargs)
    except TypeError:
        alternativas = {"product": "products", "products": "product"}
        for antiguo, nuevo in alternativas.items():
            if antiguo in kwargs:
                intento = dict(kwargs)
                valor = intento.pop(antiguo)
                if nuevo == "products" and not isinstance(valor, list):
                    valor = [valor]
                elif nuevo == "product" and isinstance(valor, list):
                    valor = valor[0]
                intento[nuevo] = valor
                try:
                    return ifcopenshell.api.run(usecase, model, **intento)
                except TypeError:
                    continue
        raise


def matriz_traslacion(x, y, z):
    return (
        (1.0, 0.0, 0.0, x),
        (0.0, 1.0, 0.0, y),
        (0.0, 0.0, 1.0, z),
        (0.0, 0.0, 0.0, 1.0),
    )


def leer_csv_agrupado(csv_path):
    elementos = {}
    with open(csv_path, newline="", encoding="utf-8-sig") as f:
        for row in csv.DictReader(f):
            id_ = row["Id"]
            if id_ not in elementos:
                elementos[id_] = {
                    "nombre": row["Nombre"],
                    "min": (float(row["MinX"]), float(row["MinY"]), float(row["MinZ"])),
                    "max": (float(row["MaxX"]), float(row["MaxY"]), float(row["MaxZ"])),
                    "categorias": defaultdict(dict),
                }
            elementos[id_]["categorias"][row["Categoria"]][row["Propiedad"]] = row["Valor"]
            if row["Categoria"] == "" and row["Propiedad"] == "":
                # Fila "en blanco" (exportada sin propiedades): no crear un Pset vacío.
                del elementos[id_]["categorias"][""]
    return elementos


def leer_obj_agrupado(obj_path):
    """
    Lee el OBJ generado por el plugin (con vértices reutilizados). Reconstruye,
    para cada grupo 'g <id>', la lista plana de vértices por triángulo
    (3 vértices por triángulo, en orden), resolviendo las líneas 'f' contra
    la lista global de vértices 'v'.
    Devuelve: (grupos, normales_por_grupo) -- ambos {id: [(x,y,z), ...]}, mismo orden/longitud
    """
    vertices_globales = []  # lista global de (x,y,z), en el orden del archivo
    normales_globales = []  # lista global de (nx,ny,nz), en el orden del archivo
    grupos = {}
    normales_por_grupo = {}
    colores_por_grupo = {}
    id_actual = None

    with open(obj_path, "r", encoding="utf-8") as f:
        for linea in f:
            linea = linea.strip()
            if not linea:
                continue

            if linea.startswith("g "):
                id_actual = linea[2:].strip()
                if id_actual not in grupos:
                    grupos[id_actual] = []
                    normales_por_grupo[id_actual] = []

            elif linea.startswith("# color "):
                partes = linea.split()
                if id_actual is not None and len(partes) >= 6:
                    colores_por_grupo[id_actual] = (
                        float(partes[2]), float(partes[3]), float(partes[4]), float(partes[5])
                    )

            elif linea.startswith("vn "):
                partes = linea.split()
                normales_globales.append((float(partes[1]), float(partes[2]), float(partes[3])))

            elif linea.startswith("v "):
                partes = linea.split()
                vertices_globales.append((float(partes[1]), float(partes[2]), float(partes[3])))

            elif linea.startswith("f "):
                if id_actual is None:
                    continue
                partes = linea.split()
                # Cada token puede venir como "i" o "i//j" (posición//normal)
                indices_pos = []
                indices_norm = []
                for token in partes[1:4]:
                    if "//" in token:
                        p, n = token.split("//")
                        indices_pos.append(int(p))
                        indices_norm.append(int(n))
                    else:
                        indices_pos.append(int(token))
                        indices_norm.append(None)

                for ip, inrm in zip(indices_pos, indices_norm):
                    grupos[id_actual].append(vertices_globales[ip - 1])
                    if inrm is not None and inrm - 1 < len(normales_globales):
                        normales_por_grupo[id_actual].append(normales_globales[inrm - 1])
                    else:
                        normales_por_grupo[id_actual].append((0.0, 0.0, 1.0))

    return grupos, normales_por_grupo, colores_por_grupo


def crear_representacion_caja(model, contexto_cuerpo, dx, dy, dz):
    dx = max(dx, 0.05)
    dy = max(dy, 0.05)
    dz = max(dz, 0.05)

    perfil = model.create_entity("IfcRectangleProfileDef", ProfileType="AREA", XDim=dx, YDim=dy)
    punto_base = model.create_entity("IfcCartesianPoint", Coordinates=(0.0, 0.0, 0.0))
    eje = model.create_entity("IfcAxis2Placement3D", Location=punto_base)
    direccion_extrusion = model.create_entity("IfcDirection", DirectionRatios=(0.0, 0.0, 1.0))

    solido = model.create_entity(
        "IfcExtrudedAreaSolid", SweptArea=perfil, Position=eje,
        ExtrudedDirection=direccion_extrusion, Depth=dz,
    )
    return model.create_entity(
        "IfcShapeRepresentation", ContextOfItems=contexto_cuerpo,
        RepresentationIdentifier="Body", RepresentationType="SweptSolid", Items=[solido],
    )


def aplicar_color(model, malla, color_rgba):
    if color_rgba is None:
        return
    r, g, b, a = color_rgba
    transparencia = max(0.0, min(1.0, 1.0 - a))  # IFC usa "qué tan transparente", no "qué tan opaco"

    color = model.create_entity("IfcColourRgb", Red=r, Green=g, Blue=b)
    shading = model.create_entity("IfcSurfaceStyleShading", SurfaceColour=color, Transparency=transparencia)
    estilo_superficie = model.create_entity("IfcSurfaceStyle", Side="BOTH", Styles=[shading])
    model.create_entity("IfcStyledItem", Item=malla, Styles=[estilo_superficie])


def crear_representacion_malla(model, contexto_cuerpo, vertices_absolutos, normales, origen, color_rgba=None, tolerancia_mm=0.5):
    # Coordenadas relativas al punto de colocación del elemento (origen = bbox min)
    ox, oy, oz = origen

    usar_normales = bool(normales) and len(normales) == len(vertices_absolutos)

    puntos_unicos = {}
    coord_list = []
    normales_unicas = []
    coord_index = []

    for i in range(0, len(vertices_absolutos) - 2, 3):
        indices_cara = []
        for j in range(3):
            x, y, z = vertices_absolutos[i + j]
            xr, yr, zr = x - ox, y - oy, z - oz

            if usar_normales:
                nx, ny, nz = normales[i + j]
                clave = (
                    round(xr / tolerancia_mm), round(yr / tolerancia_mm), round(zr / tolerancia_mm),
                    round(nx, 2), round(ny, 2), round(nz, 2),
                )
            else:
                clave = (round(xr / tolerancia_mm), round(yr / tolerancia_mm), round(zr / tolerancia_mm))

            indice_existente = puntos_unicos.get(clave)
            if indice_existente is None:
                coord_list.append((xr, yr, zr))
                if usar_normales:
                    normales_unicas.append((nx, ny, nz))
                indice_existente = len(coord_list)  # IFC usa índices base-1
                puntos_unicos[clave] = indice_existente

            indices_cara.append(indice_existente)

        # Triángulo degenerado (los 3 puntos quedaron soldados al mismo): se descarta
        if len(set(indices_cara)) == 3:
            coord_index.append(tuple(indices_cara))

    point_list = model.create_entity("IfcCartesianPointList3D", CoordList=coord_list)

    kwargs_malla = {"Coordinates": point_list, "CoordIndex": coord_index}
    if usar_normales:
        kwargs_malla["Normals"] = normales_unicas

    malla = model.create_entity("IfcTriangulatedFaceSet", **kwargs_malla)
    aplicar_color(model, malla, color_rgba)

    return model.create_entity(
        "IfcShapeRepresentation", ContextOfItems=contexto_cuerpo,
        RepresentationIdentifier="Body", RepresentationType="Tessellation", Items=[malla],
    )


def leer_csv_para_fusion(csv_path):
    """
    Devuelve {guid_ifc: {categoria: {propiedad: valor}}}
    Solo incluye elementos donde se pudo encontrar su GUID de IFC
    (columna 'GuidIfc' del CSV, generada en modo 'Modelo Revit/IFC').
    """
    elementos_por_id = defaultdict(lambda: {"guid": None, "categorias": defaultdict(dict)})

    with open(csv_path, newline="", encoding="utf-8-sig") as f:
        lector = csv.DictReader(f)
        if "GuidIfc" not in lector.fieldnames:
            print("[ERROR] Este CSV no tiene la columna 'GuidIfc'. "
                  "¿Activaste la casilla 'Modelo Revit/IFC' antes de exportar desde Navisworks?")
            sys.exit(1)

        for row in lector:
            id_ = row["Id"]
            categoria = row.get("Categoria") or ""
            propiedad = row.get("Propiedad") or ""
            valor = row.get("Valor") or ""
            guid = (row.get("GuidIfc") or "").strip()

            if guid:
                elementos_por_id[id_]["guid"] = guid

            if propiedad.strip():
                elementos_por_id[id_]["categorias"][categoria][propiedad] = valor

    por_guid = {}
    sin_guid = 0
    for id_, datos in elementos_por_id.items():
        if not datos["guid"]:
            sin_guid += 1
            continue
        por_guid[datos["guid"]] = datos["categorias"]

    print(f"{len(por_guid)} elementos con GUID identificado.")
    if sin_guid:
        print(f"[AVISO] {sin_guid} elementos NO tenían GUID de IFC (no se encontró en ningún ancestro) y se omitieron.")

    return por_guid


def main_fusion(ifc_original_path, csv_path, ifc_salida_path):
    """
    Modo Revit/IFC: inyecta en un IFC ya existente (típicamente exportado
    desde Revit) las propiedades adicionales capturadas en Navisworks,
    emparejando por GUID de IFC -- sin tocar la geometría original.
    """
    print("Leyendo CSV de propiedades de Navisworks...")
    propiedades_por_guid = leer_csv_para_fusion(csv_path)

    print("Abriendo IFC original (puede tardar con archivos grandes)...")
    model = ifcopenshell.open(ifc_original_path)

    encontrados = 0
    no_encontrados = 0
    sin_propiedades_nuevas = 0
    total = len(propiedades_por_guid)
    procesados = 0

    for guid, categorias in propiedades_por_guid.items():
        procesados += 1
        if procesados % 100 == 0:
            print(f"  ...{procesados} de {total} procesados", flush=True)

        elemento = None
        try:
            elemento = model.by_guid(guid)
        except Exception:
            elemento = None

        if elemento is None:
            no_encontrados += 1
            continue

        hubo_alguna = False
        for nombre_categoria, propiedades in categorias.items():
            propiedades_limpias = {k: v for k, v in propiedades.items() if k.strip()}
            if not propiedades_limpias:
                continue
            pset = ifcopenshell.api.run(
                "pset.add_pset", model, product=elemento, name=nombre_categoria[:50]
            )
            ifcopenshell.api.run(
                "pset.edit_pset", model, pset=pset, properties=propiedades_limpias
            )
            hubo_alguna = True

        if hubo_alguna:
            encontrados += 1
        else:
            sin_propiedades_nuevas += 1

    model.write(ifc_salida_path)

    print(f"\nListo. Guardado en: {ifc_salida_path}")
    print(f"  Elementos actualizados con propiedades nuevas: {encontrados}")
    print(f"  Elementos sin propiedades nuevas que agregar: {sin_propiedades_nuevas}")
    print(f"  GUID del CSV que no se encontraron en el IFC original: {no_encontrados}")


def main(csv_path, ifc_path, obj_path=None, limite=None, tolerancia_mm=0.5, callback_progreso=None, usar_offset=True):
    """
    callback_progreso: función opcional callback_progreso(hecho, total) para
    reportar avance a una interfaz gráfica (usada en el modo standalone).
    """
    print("Leyendo CSV...")
    elementos = leer_csv_agrupado(csv_path)
    print(f"{len(elementos)} elementos encontrados en el CSV.")

    if limite:
        elementos = dict(list(elementos.items())[:limite])
        print(f"MODO PRUEBA: usando solo los primeros {len(elementos)} elementos.")

    geometrias = {}
    normales_geometrias = {}
    colores_geometrias = {}
    if obj_path:
        print("Leyendo geometría OBJ...")
        geometrias, normales_geometrias, colores_geometrias = leer_obj_agrupado(obj_path)
        print(f"{len(geometrias)} elementos con geometría real encontrados en el OBJ.")

    print(f"Tolerancia de soldadura de puntos: {tolerancia_mm}mm")

    total_elementos = len(elementos)

    try:
        model = ifcopenshell.api.run("project.create_file", version="IFC4")
    except TypeError:
        model = ifcopenshell.api.run("project.create_file")

    api_flex("root.create_entity", model, ifc_class="IfcProject", name="Modelo desde NWD")

    # Declaramos explícitamente las unidades como MILÍMETROS, porque los
    # números que vienen de Navisworks están en esa escala. Si no hacemos
    # esto, el archivo por defecto asume metros y todo sale 1000 veces más
    # grande de lo real (por eso no se veía nada en el visor).
    unidad_longitud = model.create_entity("IfcSIUnit", UnitType="LENGTHUNIT", Prefix="MILLI", Name="METRE")
    asignacion_unidades = model.create_entity("IfcUnitAssignment", Units=[unidad_longitud])
    project_temp = model.by_type("IfcProject")[0]
    project_temp.UnitsInContext = asignacion_unidades

    contexto = api_flex("context.add_context", model, context_type="Model")
    contexto_cuerpo = api_flex(
        "context.add_context", model, context_type="Model",
        context_identifier="Body", target_view="MODEL_VIEW", parent=contexto,
    )

    project = model.by_type("IfcProject")[0]
    site = api_flex("root.create_entity", model, ifc_class="IfcSite", name="Site")
    building = api_flex("root.create_entity", model, ifc_class="IfcBuilding", name="Building")
    storey = api_flex("root.create_entity", model, ifc_class="IfcBuildingStorey", name="Storey")

    api_flex("aggregate.assign_object", model, relating_object=project, products=[site])
    api_flex("aggregate.assign_object", model, relating_object=site, products=[building])
    api_flex("aggregate.assign_object", model, relating_object=building, products=[storey])

    contador = 0
    con_geometria_real = 0
    omitidos_sin_geometria = 0
    offset = None
    procesados_total = 0

    for id_, datos in elementos.items():
        procesados_total += 1
        # Si no hay geometría real capturada para este elemento, lo omitimos
        # por completo (ni pieza, ni propiedades) para no inflar el archivo.
        if id_ not in geometrias or len(geometrias[id_]) < 3:
            omitidos_sin_geometria += 1
            if callback_progreso and procesados_total % 20 == 0:
                callback_progreso(procesados_total, total_elementos)
            continue

        proxy = api_flex("root.create_entity", model, ifc_class="IfcBuildingElementProxy", name=datos["nombre"])
        api_flex("spatial.assign_container", model, relating_structure=storey, products=[proxy])

        minx, miny, minz = datos["min"]
        maxx, maxy, maxz = datos["max"]

        if offset is None:
            if usar_offset:
                offset = (minx, miny, minz)
                print(f"Desplazamiento global aplicado (para evitar problemas de precisión en visores): {offset}")
            else:
                offset = (0.0, 0.0, 0.0)
                print("Sin desplazamiento: se conservan las coordenadas reales originales.")

        minx_rel = (minx - offset[0]) / 1000.0
        miny_rel = (miny - offset[1]) / 1000.0
        minz_rel = (minz - offset[2]) / 1000.0

        api_flex("geometry.edit_object_placement", model, product=proxy, matrix=matriz_traslacion(minx_rel, miny_rel, minz_rel))

        try:
            normales_elemento = normales_geometrias.get(id_, [])
            color_elemento = colores_geometrias.get(id_)

            representacion = crear_representacion_malla(
                model, contexto_cuerpo, geometrias[id_], normales_elemento, (minx, miny, minz), color_elemento, tolerancia_mm
            )
            api_flex("geometry.assign_representation", model, product=proxy, representation=representacion)
            con_geometria_real += 1
        except Exception as e:
            print(f"[AVISO] Error de geometría para '{datos['nombre']}': {e}")

        for nombre_categoria, propiedades in datos["categorias"].items():
            pset = api_flex("pset.add_pset", model, product=proxy, name=nombre_categoria[:50])
            api_flex("pset.edit_pset", model, pset=pset, properties=propiedades)

        contador += 1
        if contador % 100 == 0:
            print(f"  ...{contador} elementos procesados", flush=True)
        if callback_progreso and procesados_total % 20 == 0:
            callback_progreso(procesados_total, total_elementos)

    model.write(ifc_path)
    print(f"\nListo. {contador} elementos escritos en {ifc_path}")
    print(f"  Omitidos por no tener geometría real: {omitidos_sin_geometria}")
    print(f"  Con geometría real (visible): {con_geometria_real}")


def ejecutar_modo_grafico():
    import threading
    import tkinter as tk
    from tkinter import filedialog, messagebox, ttk

    raiz = tk.Tk()
    raiz.withdraw()

    csv_path = filedialog.askopenfilename(
        title="Selecciona el archivo de propiedades (.csv)",
        filetypes=[("CSV", "*.csv")],
    )
    if not csv_path:
        return

    obj_path = filedialog.askopenfilename(
        title="Selecciona el archivo de geometría (.obj) - opcional, cancela si no tienes",
        filetypes=[("OBJ", "*.obj")],
    )
    obj_path = obj_path if obj_path else None

    ifc_path = filedialog.asksaveasfilename(
        title="¿Dónde guardar el IFC final?",
        defaultextension=".ifc",
        filetypes=[("IFC", "*.ifc")],
    )
    if not ifc_path:
        return

    raiz.destroy()

    # Ventana de progreso
    ventana = tk.Tk()
    ventana.title("Generando IFC...")
    ventana.geometry("420x120")
    ventana.resizable(False, False)

    etiqueta = tk.Label(ventana, text="Procesando 0 de 0 elementos...")
    etiqueta.pack(pady=15)

    barra = ttk.Progressbar(ventana, orient="horizontal", length=380, mode="determinate")
    barra.pack(pady=5)

    def actualizar_progreso(hecho, total):
        def _actualizar():
            barra["maximum"] = max(total, 1)
            barra["value"] = hecho
            etiqueta.config(text=f"Procesando {hecho} de {total} elementos...")
        ventana.after(0, _actualizar)

    def trabajar():
        try:
            main(csv_path, ifc_path, obj_path, None, 0.5, actualizar_progreso)
            ventana.after(0, lambda: [
                etiqueta.config(text="¡Listo!"),
                messagebox.showinfo("Completado", f"IFC generado en:\n{ifc_path}"),
                ventana.destroy(),
            ])
        except Exception as e:
            ventana.after(0, lambda: [
                messagebox.showerror("Error", str(e)),
                ventana.destroy(),
            ])

    hilo = threading.Thread(target=trabajar, daemon=True)
    hilo.start()
    ventana.mainloop()


if __name__ == "__main__":
    if len(sys.argv) == 1:
        # Sin argumentos: modo gráfico (doble clic directo en el .exe)
        ejecutar_modo_grafico()
    elif sys.argv[1] == "--fusionar":
        # Modo Revit/IFC: python construir_ifc.py --fusionar "original.ifc" "propiedades.csv" "salida.ifc"
        if len(sys.argv) != 5:
            print('Uso: python construir_ifc.py --fusionar "original.ifc" "propiedades.csv" "salida.ifc"')
            sys.exit(1)
        main_fusion(sys.argv[2], sys.argv[3], sys.argv[4])
    else:
        if len(sys.argv) not in (3, 4, 5, 6, 7):
            print('Uso: python construir_ifc.py "propiedades.csv" "salida.ifc" ["geometria.obj"] [limite_elementos] [tolerancia_mm] [usar_offset:1/0]')
            sys.exit(1)
        obj_arg = sys.argv[3] if len(sys.argv) >= 4 else None
        limite_arg = int(sys.argv[4]) if len(sys.argv) >= 5 else None
        if limite_arg == 0:
            limite_arg = None
        tolerancia_arg = float(sys.argv[5]) if len(sys.argv) >= 6 else 0.5
        usar_offset_arg = (sys.argv[6] != "0") if len(sys.argv) == 7 else True
        main(sys.argv[1], sys.argv[2], obj_arg, limite_arg, tolerancia_arg, None, usar_offset_arg)
