"""
Escanea TODO el archivo .obj y clasifica cada grupo (elemento) según la
cantidad ABSOLUTA de puntos únicos que tiene -- para saber qué proporción
del modelo tiene el problema de geometría tipo caja (muy pocos puntos
únicos, como 7-11) frente a los que tienen geometría real y detallada
(cientos de puntos únicos), y si hay algún patrón en las dimensiones.

Uso:
    python escanear_calidad_geometria.py "geometria.obj" [umbral_puntos]

umbral_puntos: cantidad mínima de puntos únicos para considerar un
elemento "detallado" (por defecto 20).
"""

import sys
from collections import defaultdict


def main(obj_path, umbral_puntos=20):
    vertices_globales = []
    grupo_vertices = defaultdict(list)
    id_actual = None

    with open(obj_path, "r", encoding="utf-8") as f:
        for linea in f:
            linea = linea.strip()
            if not linea:
                continue
            if linea.startswith("g "):
                id_actual = linea[2:].strip()
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

    detallados = []
    en_caja = []

    for gid, verts in grupo_vertices.items():
        if len(verts) < 3:
            continue
        unicos = set((round(v[0], 3), round(v[1], 3), round(v[2], 3)) for v in verts)

        xs = [v[0] for v in verts]
        ys = [v[1] for v in verts]
        zs = [v[2] for v in verts]
        dims = sorted([max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)])

        info = {
            "id": gid,
            "triangulos": len(verts) // 3,
            "unicos": len(unicos),
            "dims": dims,
        }

        if len(unicos) >= umbral_puntos:
            detallados.append(info)
        else:
            en_caja.append(info)

    total = len(detallados) + len(en_caja)
    print(f"Total de elementos con geometría: {total}")
    print(f"Detallados (puntos únicos >= {umbral_puntos}): {len(detallados)} ({100*len(detallados)/total:.1f}%)")
    print(f"En caja (puntos únicos < {umbral_puntos}): {len(en_caja)} ({100*len(en_caja)/total:.1f}%)")

    if en_caja:
        dims_en_caja = [info["dims"] for info in en_caja]
        largos = [d[2] for d in dims_en_caja]
        cortos = [d[0] for d in dims_en_caja]
        print(f"\nDimensiones de los 'en caja' (más larga): min={min(largos):.2f}, max={max(largos):.2f}, promedio={sum(largos)/len(largos):.2f}")
        print(f"Dimensiones de los 'en caja' (más corta): min={min(cortos):.2f}, max={max(cortos):.2f}, promedio={sum(cortos)/len(cortos):.2f}")

    if detallados:
        dims_detallados = [info["dims"] for info in detallados]
        largos = [d[2] for d in dims_detallados]
        cortos = [d[0] for d in dims_detallados]
        print(f"\nDimensiones de los 'detallados' (más larga): min={min(largos):.2f}, max={max(largos):.2f}, promedio={sum(largos)/len(largos):.2f}")
        print(f"Dimensiones de los 'detallados' (más corta): min={min(cortos):.2f}, max={max(cortos):.2f}, promedio={sum(cortos)/len(cortos):.2f}")

    print("\n--- Primeros 10 'en caja' (para revisar patrones) ---")
    for info in en_caja[:10]:
        print(f"  Id {info['id']}: {info['triangulos']} tri, {info['unicos']} únicos, "
              f"dims={[round(d,2) for d in info['dims']]}")

    print("\n--- Primeros 10 'detallados' (para revisar patrones) ---")
    for info in detallados[:10]:
        print(f"  Id {info['id']}: {info['triangulos']} tri, {info['unicos']} únicos, "
              f"dims={[round(d,2) for d in info['dims']]}")


if __name__ == "__main__":
    if len(sys.argv) not in (2, 3):
        print('Uso: python escanear_calidad_geometria.py "geometria.obj" [umbral_puntos]')
        sys.exit(1)
    umbral = float(sys.argv[2]) if len(sys.argv) == 3 else 20
    main(sys.argv[1], umbral)

