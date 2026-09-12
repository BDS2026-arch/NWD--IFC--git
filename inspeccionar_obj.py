"""
Inspecciona a fondo el archivo OBJ generado por el plugin de Navisworks,
sin construir ningún IFC. Reporta datos crudos para diagnosticar dónde
está el problema real.

Uso:
    python inspeccionar_obj.py "geometria_export.obj"
"""

import sys
from collections import defaultdict


def main(obj_path):
    vertices_globales = []
    grupos_orden = []          # orden en que aparecen los "g <id>" en el archivo
    grupos_bloques = defaultdict(int)   # cuántas veces aparece "g <id>" (no contiguo = sospechoso)
    grupo_vertices = defaultdict(list)  # id -> lista de (x,y,z) de sus caras
    grupo_min_indice = {}
    grupo_max_indice = {}
    grupo_tiene_normales = {}   # id -> True/False (si sus caras usan formato i//j)
    grupo_num_triangulos = defaultdict(int)

    id_actual = None
    contador_v = 0
    contador_vn = 0
    contador_f = 0
    contador_g = 0
    indices_fuera_de_rango = 0
    lineas_no_reconocidas = 0

    with open(obj_path, "r", encoding="utf-8") as f:
        for num_linea, linea in enumerate(f, start=1):
            linea = linea.strip()
            if not linea:
                continue

            if linea.startswith("g "):
                contador_g += 1
                nuevo_id = linea[2:].strip()
                if grupos_bloques[nuevo_id] == 0:
                    grupos_orden.append(nuevo_id)
                grupos_bloques[nuevo_id] += 1
                id_actual = nuevo_id

            elif linea.startswith("vn "):
                contador_vn += 1

            elif linea.startswith("v "):
                contador_v += 1
                partes = linea.split()
                vertices_globales.append((float(partes[1]), float(partes[2]), float(partes[3])))

            elif linea.startswith("f "):
                contador_f += 1
                if id_actual is None:
                    continue
                partes = linea.split()
                tiene_normal = "//" in partes[1]
                if id_actual not in grupo_tiene_normales:
                    grupo_tiene_normales[id_actual] = tiene_normal
                grupo_num_triangulos[id_actual] += 1

                # Cada token puede ser "i" o "i//j" (posición//normal) -- nos quedamos con la posición
                indices = [int(p.split("//")[0]) for p in partes[1:4]]

                for idx in indices:
                    if idx < 1 or idx > len(vertices_globales):
                        indices_fuera_de_rango += 1
                    else:
                        grupo_vertices[id_actual].append(vertices_globales[idx - 1])

                mn = min(indices)
                mx = max(indices)
                if id_actual not in grupo_min_indice or mn < grupo_min_indice[id_actual]:
                    grupo_min_indice[id_actual] = mn
                if id_actual not in grupo_max_indice or mx > grupo_max_indice[id_actual]:
                    grupo_max_indice[id_actual] = mx

            else:
                lineas_no_reconocidas += 1

    print("=== Resumen general ===")
    print(f"Líneas 'g': {contador_g}")
    print(f"Líneas 'v': {contador_v}")
    print(f"Líneas 'vn': {contador_vn}")
    print(f"Líneas 'f': {contador_f}")
    print(f"Vértices acumulados: {len(vertices_globales)}")
    print(f"Índices de cara fuera de rango: {indices_fuera_de_rango}")
    print(f"Líneas no reconocidas: {lineas_no_reconocidas}")
    print(f"Grupos distintos (por id): {len(grupos_orden)}")

    ids_repetidos = [gid for gid, veces in grupos_bloques.items() if veces > 1]
    print(f"IDs cuyo bloque 'g' aparece más de una vez (no contiguo): {len(ids_repetidos)}")
    if ids_repetidos:
        print(f"  Ejemplos: {ids_repetidos[:10]}")

    # --- Nuevo: desglose de suavizado ---
    con_normales = [gid for gid in grupos_orden if grupo_tiene_normales.get(gid) is True]
    sin_normales = [gid for gid in grupos_orden if grupo_tiene_normales.get(gid) is False]

    def stats_triangulos(lista_ids):
        conteos = [grupo_num_triangulos[gid] for gid in lista_ids]
        if not conteos:
            return "sin datos"
        return f"min={min(conteos)}, max={max(conteos)}, promedio={sum(conteos)/len(conteos):.1f}"

    print(f"\n=== Desglose de suavizado ===")
    print(f"Grupos CON normales (suavizados): {len(con_normales)}  | triángulos: {stats_triangulos(con_normales)}")
    print(f"Grupos SIN normales (sin suavizar): {len(sin_normales)}  | triángulos: {stats_triangulos(sin_normales)}")
    if sin_normales:
        print(f"  Primeros 10 IDs sin suavizar: {sin_normales[:10]}")
        print(f"  Sus cantidades de triángulos: {[grupo_num_triangulos[g] for g in sin_normales[:10]]}")

    print("\n=== Bounding box y centroide de los primeros 15 grupos (en orden de aparición) ===")
    for gid in grupos_orden[:15]:
        verts = grupo_vertices.get(gid, [])
        if not verts:
            print(f"  Grupo {gid}: SIN VÉRTICES VÁLIDOS")
            continue
        xs = [v[0] for v in verts]
        ys = [v[1] for v in verts]
        zs = [v[2] for v in verts]
        cx = sum(xs) / len(xs)
        cy = sum(ys) / len(ys)
        cz = sum(zs) / len(zs)
        print(f"  Grupo {gid}: {len(verts)} verts | tri={grupo_num_triangulos[gid]} | "
              f"suavizado={grupo_tiene_normales.get(gid)} | "
              f"min=({min(xs):.2f},{min(ys):.2f},{min(zs):.2f}) | "
              f"max=({max(xs):.2f},{max(ys):.2f},{max(zs):.2f}) | "
              f"centroide=({cx:.2f},{cy:.2f},{cz:.2f})")

    print("\n=== Distancia entre centroides de grupos consecutivos ===")
    centroides = []
    for gid in grupos_orden[:15]:
        verts = grupo_vertices.get(gid, [])
        if not verts:
            continue
        xs = [v[0] for v in verts]
        ys = [v[1] for v in verts]
        zs = [v[2] for v in verts]
        centroides.append((gid, sum(xs) / len(xs), sum(ys) / len(ys), sum(zs) / len(zs)))

    for i in range(1, len(centroides)):
        gid0, x0, y0, z0 = centroides[i - 1]
        gid1, x1, y1, z1 = centroides[i]
        dist = ((x1 - x0) ** 2 + (y1 - y0) ** 2 + (z1 - z0) ** 2) ** 0.5
        print(f"  {gid0} -> {gid1}: distancia = {dist:.2f}")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print('Uso: python inspeccionar_obj.py "geometria.obj"')
        sys.exit(1)
    main(sys.argv[1])
