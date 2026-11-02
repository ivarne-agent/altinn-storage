-- FINAL CLEANUP of the storage-accounts change. Deploy this ONLY after every earlier portion of the
-- change (v0.39 and the code that calls the _v2/_v3 functions) is running in EVERY environment.
-- Deploys roll: while this migration runs, pods still on the previous release call the old function
-- versions dropped below, and they fail once these DROPs commit. Never ship this with the code
-- portions; ship it in a later release, after the earlier ones are fully rolled out.

-- Fail the startup migration fast instead of queuing behind other locks; the pod restarts and retries.
SET LOCAL lock_timeout = '10s';

-- VALIDATE takes only SHARE UPDATE EXCLUSIVE, so reads and writes continue during the scan.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'dataelementblobversions_storageaccountid_fkey'
          AND conrelid = 'storage.dataelementblobversions'::regclass
          AND NOT convalidated) THEN
        ALTER TABLE storage.dataelementblobversions VALIDATE CONSTRAINT dataelementblobversions_storageaccountid_fkey;
    END IF;
END $$;

-- Function versions superseded by the storage-accounts change.
DROP FUNCTION IF EXISTS storage.createblobversion(uuid, uuid, uuid, text, text, integer);
DROP FUNCTION IF EXISTS storage.readblobversion(uuid, uuid, uuid);
DROP FUNCTION IF EXISTS storage.readblobversions(uuid, uuid);
DROP FUNCTION IF EXISTS storage.readblobversionsforinstance(uuid);
DROP FUNCTION IF EXISTS storage.readdetachedblobversions(uuid);
DROP FUNCTION IF EXISTS storage.readorphanblobversionsforcleanup();
DROP FUNCTION IF EXISTS storage.readharddeleteddataelementsforcleanup();
DROP FUNCTION IF EXISTS storage.readdataelement_v2(uuid);
DROP FUNCTION IF EXISTS storage.readinstance_v2(uuid);
DROP FUNCTION IF EXISTS storage.applyinstancemutation(uuid, bigint, integer, integer, uuid, timestamptz, text, jsonb, jsonb, jsonb, jsonb, jsonb, jsonb);
