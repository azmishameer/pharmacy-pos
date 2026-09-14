-- Run against an isolated database after applying InitialCatalogue. Fixtures roll back.
\set ON_ERROR_STOP on
BEGIN;
INSERT INTO manufacturers VALUES ('10000000-0000-0000-0000-000000000001', 'Test manufacturer', true);
INSERT INTO dosage_forms VALUES ('20000000-0000-0000-0000-000000000001', 'Tablet', true);
INSERT INTO generic_ingredients VALUES
 ('30000000-0000-0000-0000-000000000001', 'Empagliflozin', true),
 ('30000000-0000-0000-0000-000000000002', 'Metformin Hydrochloride', true);
INSERT INTO medicines (id, brand_name, manufacturer_id, dosage_form_id, classification, base_unit, is_active) VALUES (
 '40000000-0000-0000-0000-000000000001', 'Jardimet example',
 '10000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000001',
 'Prescription', 'Tablet', true);
INSERT INTO medicine_ingredients VALUES
 ('40000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000001', 5, 'mg', 0),
 ('40000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000002', 500, 'mg', 1);

DO $$
BEGIN
 IF (SELECT array_agg(strength_value ORDER BY display_order) FROM medicine_ingredients
     WHERE medicine_id = '40000000-0000-0000-0000-000000000001') <> ARRAY[5, 500]::numeric[] THEN
   RAISE EXCEPTION 'Combination strengths did not round-trip';
 END IF;
 BEGIN
   UPDATE medicine_ingredients SET strength_value = 0 WHERE display_order = 0;
   RAISE EXCEPTION 'Zero strength was accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
   UPDATE medicines SET classification = 'Unknown';
   RAISE EXCEPTION 'Unknown classification was accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
   UPDATE medicines SET manufacturer_id = '10000000-0000-0000-0000-000000000099';
   RAISE EXCEPTION 'Missing manufacturer was accepted';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
   DELETE FROM manufacturers WHERE id = '10000000-0000-0000-0000-000000000001';
   RAISE EXCEPTION 'Referenced manufacturer was deleted';
 EXCEPTION WHEN foreign_key_violation OR restrict_violation THEN NULL; END;
 BEGIN
   DELETE FROM medicines WHERE id = '40000000-0000-0000-0000-000000000001';
   RAISE EXCEPTION 'Medicine with ingredient history was deleted';
 EXCEPTION WHEN foreign_key_violation OR restrict_violation THEN NULL; END;
 BEGIN
   UPDATE medicine_ingredients SET display_order = 0 WHERE display_order = 1;
   RAISE EXCEPTION 'Duplicate ingredient order was accepted';
 EXCEPTION WHEN unique_violation THEN NULL; END;
 BEGIN
   UPDATE medicines SET brand_name = '   ';
   RAISE EXCEPTION 'Blank brand was accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
END $$;
ROLLBACK;
\echo Catalogue constraint checks passed.
