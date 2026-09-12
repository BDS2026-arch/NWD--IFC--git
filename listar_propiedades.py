"""
Lista todas las propiedades (categoría, nombre, valor) de un elemento
específico, dado su Id en el CSV.

Uso:
    python listar_propiedades.py "propiedades.csv" ID
"""

import csv
import sys


def main(csv_path, id_buscado):
    encontrado = False
    with open(csv_path, newline="", encoding="utf-8-sig") as f:
        for row in csv.DictReader(f):
            if row["Id"] == id_buscado:
                if not encontrado:
                    print(f"--- Elemento Id {id_buscado}: '{row['Nombre']}' ---")
                    print(f"BBox: min=({row['MinX']},{row['MinY']},{row['MinZ']}) "
                          f"max=({row['MaxX']},{row['MaxY']},{row['MaxZ']})")
                    print()
                    encontrado = True
                print(f"  [{row['Categoria']}] {row['Propiedad']} = {row['Valor']}")

    if not encontrado:
        print(f"No se encontró ningún elemento con Id {id_buscado}.")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print('Uso: python listar_propiedades.py "propiedades.csv" ID')
        sys.exit(1)
    main(sys.argv[1], sys.argv[2])
