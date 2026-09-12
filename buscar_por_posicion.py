"""
Busca, en un .obj, el grupo cuyo centro esté más cerca de una coordenada
dada. Útil para volver a encontrar el "mismo" elemento entre dos
exportaciones distintas, cuando no tenemos nombre/propiedades para
buscarlo (ej. porque no se cargaron las propiedades esta vez).

Uso:
    python buscar_por_posicion.py "geometria.obj" x y z
"""

import sys


def main(obj_path, x_obj, y_obj, z_obj):
    vertices_globales = []
    grupo_vertices = {}
    id_actual = None

    with open(obj_path, "r", encoding="utf-8") as f:
        for linea in f:
            linea = linea.strip()
            if not linea:
                continue
            if linea.startswith("g "):
                id_actual = linea[2:].strip()
                if id_actual not in grupo_vertices:
                    grupo_vertices[id_actual] = []
            elif linea.startswith("v "):
                partes = linea.split()
                vertices_globales.append((float(partes[1]), float(partes[2]), float(partes[3])))
            elif linea.startswith("f "):
                if id_actual is None:
                    continue
                partes = linea.split()
                for token in partes[1:4]:
                    idx = int(token.split("//")[0])
                    grupo_vertices[id_actual].append(vertices_globales[idx - 1])

    mejor_id = None
    mejor_distancia = None
    mejor_centro = None

    for gid, verts in grupo_vertices.items():
        if not verts:
            continue
        cx = sum(v[0] for v in verts) / len(verts)
        cy = sum(v[1] for v in verts) / len(verts)
        cz = sum(v[2] for v in verts) / len(verts)
        dist = ((cx - x_obj) ** 2 + (cy - y_obj) ** 2 + (cz - z_obj) ** 2) ** 0.5
        if mejor_distancia is None or dist < mejor_distancia:
            mejor_distancia = dist
            mejor_id = gid
            mejor_centro = (cx, cy, cz)

    if mejor_id is None:
        print("No se encontró ningún grupo con geometría.")
        return

    verts = grupo_vertices[mejor_id]
    print(f"Grupo más cercano: Id {mejor_id}  (distancia al punto buscado: {mejor_distancia:.3f})")
    print(f"Centro: {mejor_centro}")
    print(f"Triángulos: {len(verts) // 3}  |  Vértices (con repetición): {len(verts)}")

    unicos = set((round(v[0], 3), round(v[1], 3), round(v[2], 3)) for v in verts)
    print(f"Puntos únicos (redondeados a 3 decimales): {len(unicos)} de {len(verts)} totales")
    print("Primeros 20 puntos únicos:")
    for p in list(unicos)[:20]:
        print(f"  {p}")


if __name__ == "__main__":
    if len(sys.argv) != 5:
        print('Uso: python buscar_por_posicion.py "geometria.obj" x y z')
        sys.exit(1)
    main(sys.argv[1], float(sys.argv[2]), float(sys.argv[3]), float(sys.argv[4]))
