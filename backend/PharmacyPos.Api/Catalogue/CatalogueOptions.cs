namespace PharmacyPos.Api.Catalogue;

public static class CatalogueOptions
{
    // Curated Bangladesh starter list; not a live register or licence validation.
    // Source: https://www.bapi-bd.com/members-directory.html (2026-09-15).
    public static readonly string[] Manufacturers = [
        "ACI Healthcare Limited", "Advanced Chemical Industries PLC", "Aristopharma Ltd.",
        "Beacon Pharmaceuticals PLC", "Beximco Pharmaceuticals PLC", "Biopharma Limited",
        "Delta Pharma Limited", "Drug International Limited", "Eskayef Pharmaceuticals Ltd.",
        "Essential Drugs Company Limited", "General Pharmaceuticals Ltd.", "Healthcare Pharmaceuticals Ltd.",
        "Incepta Pharmaceuticals Ltd.", "Navana Pharmaceuticals PLC", "NIPRO JMI Pharma Ltd.",
        "Nuvista Pharma PLC", "Opsonin Pharma Ltd.", "Orion Pharma Limited", "Popular Pharmaceuticals Ltd.",
        "Radiant Pharmaceuticals Ltd.", "Renata PLC", "Square Pharmaceuticals PLC",
        "The ACME Laboratories Ltd.", "The IBN SINA Pharmaceutical Industry PLC",
        "Unimed Unihealth Pharmaceuticals Ltd.", "Ziska Pharmaceuticals Ltd."
    ];
    public static readonly string[] Forms = ["Tablet", "Capsule", "Syrup", "Suspension", "Solution", "Drops", "Injection", "Infusion", "Cream", "Ointment", "Gel", "Lotion", "Powder", "Granules", "Inhaler", "Spray", "Suppository", "Pessary", "Patch"];
}
