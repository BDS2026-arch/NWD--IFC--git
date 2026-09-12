"""
Busca un elemento en un IFC por su GlobalId, y muestra su nombre,
categoría de propiedades, y estadísticas de su geometría (cantidad de
puntos, si tiene normales, etc.) -- para comparar contra otros elementos.

Uso:
    python buscar_por_globalid.py "salida.ifc" GLOBALID
"""

import sys
import ifcopenshell


def main(ifc_path, globalid):
    print("Abriendo IFC...")
    model = ifcopenshell.open(ifc_path)

    elemento = None
    try:
        elemento = model.by_guid(globalid)
    except Exception:
        elemento = None

    if elemento is None:
        print(f"No se encontró ningún elemento con GlobalId {globalid}.")
        return

    print(f"--- Encontrado (tipo bruto): {elemento.is_a()} ---")

    # Si el GlobalId corresponde a una ficha de propiedades (Pset), no a la
    # pieza física, buscamos el elemento real al que está conectada.
    if elemento.is_a("IfcPropertySet"):
        print("Este GlobalId es de una FICHA DE PROPIEDADES (Pset), no de la pieza física.")
        print("Buscando el elemento real al que pertenece...")
        elemento_real = None
        for rel in model.by_type("IfcRelDefinesByProperties"):
            if rel.RelatingPropertyDefinition == elemento:
                objetos = rel.RelatedObjects
                if objetos:
                    elemento_real = objetos[0]
                    break
        if elemento_real is None:
            print("No se encontró ningún elemento conectado a esta ficha.")
            return
        elemento = elemento_real
        print(f"Elemento real encontrado: {elemento.Name}  (GlobalId real: {elemento.GlobalId})")

    print(f"\n--- Elemento a analizar ---")
    print(f"Nombre: {elemento.Name}")
    print(f"Tipo IFC: {elemento.is_a()}")
    print(f"GlobalId: {elemento.GlobalId}")

    if elemento.ObjectPlacement is not None and elemento.ObjectPlacement.is_a("IfcLocalPlacement"):
        rel = elemento.ObjectPlacement.RelativePlacement
        if rel and rel.Location:
            print(f"Posición: {rel.Location.Coordinates}")

    if elemento.Representation is None:
        print("Sin geometría (Representation = None).")
        return

    for rep in elemento.Representation.Representations:
        print(f"RepresentationType: {rep.RepresentationType}")
        for item in rep.Items:
            print(f"  Tipo de item: {item.is_a()}")
            if item.is_a("IfcTriangulatedFaceSet"):
                coords = item.Coordinates.CoordList
                print(f"  Cantidad de puntos: {len(coords)}")
                print(f"  Cantidad de caras: {len(item.CoordIndex)}")
                tiene_normales = item.Normals is not None and len(item.Normals) > 0
                print(f"  Tiene normales (suavizado): {tiene_normales}")
                unicos = set((round(c[0], 3), round(c[1], 3), round(c[2], 3)) for c in coords)
                print(f"  Puntos únicos: {len(unicos)} de {len(coords)}")

                xs = [c[0] for c in coords]
                ys = [c[1] for c in coords]
                zs = [c[2] for c in coords]
                print(f"  Tamaño (largo x ancho x alto): "
                      f"{max(xs)-min(xs):.3f} x {max(ys)-min(ys):.3f} x {max(zs)-min(zs):.3f}")

    # También buscar sus propiedades (Psets)
    print("\n--- Propiedades ---")
    for definicion in elemento.IsDefinedBy:
        if definicion.is_a("IfcRelDefinesByProperties"):
            pset = definicion.RelatingPropertyDefinition
            if pset.is_a("IfcPropertySet"):
                print(f"[{pset.Name}]")
                for prop in pset.HasProperties:
                    valor = prop.NominalValue.wrappedValue if prop.NominalValue else None
                    print(f"  {prop.Name} = {valor}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print('Uso: python buscar_por_globalid.py "salida.ifc" GLOBALID')
        sys.exit(1)
    main(sys.argv[1], sys.argv[2])
