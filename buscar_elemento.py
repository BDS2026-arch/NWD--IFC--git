"""
Busca elementos por parte de su nombre en el CSV, y muestra los datos
crudos de su geometría desde el OBJ (cantidad de vértices/triángulos,
y las coordenadas de los primeros puntos, para ver a simple vista si
forman un círculo o una forma más simple).

Uso:
    python buscar_elemento.py "propiedades.csv" "geometria.obj" "texto a buscar en el nombre"
"""

import csv
import sys


def main(csv_path, obj_path, texto_buscar):
    texto_buscar = texto_buscar.lower()
    ids_encontrados = {}

    with open(csv_path, newline="", encoding="utf-8-sig") as f:
        for row in csv.DictReader(f):
            coincide = (
                texto_buscar in row["Nombre"].lower()
                or texto_buscar in row.get("Valor", "").lower()
            )
            if coincide and row["Id"] not in ids_encontrados:
                ids_encontrados[row["Id"]] = row["Nombre"]

    print(f"{len(ids_encontrados)} elementos encontrados con '{texto_buscar}' en el nombre.")
    if not ids_encontrados:
        return
    if len(ids_encontrados) > 5:
        print("(Mostrando datos solo de los primeros 5)")

    ids_interes = set(list(ids_encontrados.keys())[:5])

    # Leer el OBJ y extraer vértices por grupo, solo para los IDs de interés
    vertices_globales = []
    grupo_vertices = {}
    grupo_normales = {}
    id_actual = None

    with open(obj_path, "r", encoding="utf-8") as f:
        for linea in f:
            linea = linea.strip()
            if not linea:
                continue
            if linea.startswith("g "):
                id_actual = linea[2:].strip()
                if id_actual in ids_interes and id_actual not in grupo_vertices:
                    grupo_vertices[id_actual] = []
                    grupo_normales[id_actual] = []
            elif linea.startswith("v "):
                partes = linea.split()
                vertices_globales.append((float(partes[1]), float(partes[2]), float(partes[3])))
            elif linea.startswith("f "):
                if id_actual not in ids_interes:
                    continue
                partes = linea.split()
                for token in partes[1:4]:
                    idx = int(token.split("//")[0])
                    grupo_vertices[id_actual].append(vertices_globales[idx - 1])

    for id_, nombre in list(ids_encontrados.items())[:5]:
        verts = grupo_vertices.get(id_, [])
        print(f"\n--- Id {id_}: '{nombre}' ---")
        print(f"Triángulos: {len(verts) // 3}  |  Vértices (con repetición): {len(verts)}")
        if not verts:
            print("  SIN GEOMETRÍA en el OBJ.")
            continue

        xs = [v[0] for v in verts]
        ys = [v[1] for v in verts]
        zs = [v[2] for v in verts]
        print(f"  bbox: x=[{min(xs):.3f},{max(xs):.3f}] y=[{min(ys):.3f},{max(ys):.3f}] z=[{min(zs):.3f},{max(zs):.3f}]")

        # Puntos únicos (redondeados a 3 decimales) -- si hay muy pocos puntos
        # únicos comparado con el total, la forma es genuinamente poligonal.
        unicos = set((round(v[0], 3), round(v[1], 3), round(v[2], 3)) for v in verts)
        print(f"  Puntos únicos (redondeados a 3 decimales): {len(unicos)} de {len(verts)} totales")

        print("  Primeros 20 puntos únicos (para ver si describen un círculo):")
        for p in list(unicos)[:20]:
            print(f"    {p}")


if __name__ == "__main__":
    if len(sys.argv) != 4:
        print('Uso: python buscar_elemento.py "propiedades.csv" "geometria.obj" "texto a buscar"')
        sys.exit(1)
    main(sys.argv[1], sys.argv[2], sys.argv[3])
