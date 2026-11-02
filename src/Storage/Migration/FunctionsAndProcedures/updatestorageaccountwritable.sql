CREATE OR REPLACE FUNCTION storage.updatestorageaccountwritable(_id SMALLINT, _writable BOOLEAN)
    RETURNS BOOLEAN
    LANGUAGE plpgsql
AS $BODY$
DECLARE
    _org TEXT;
    _storageaccountnumber INT;
BEGIN
    SELECT sa.org, sa.storageaccountnumber
    INTO _org, _storageaccountnumber
    FROM storage.storageaccounts sa
    WHERE sa.id = _id
    FOR UPDATE;

    IF NOT FOUND THEN
        RETURN false;
    END IF;

    -- At most one row per (org, number) is writable, so the row takes the flag over.
    IF _writable THEN
        UPDATE storage.storageaccounts
        SET writable = false
        WHERE org = _org
            AND storageaccountnumber = _storageaccountnumber
            AND writable
            AND id <> _id;
    END IF;

    UPDATE storage.storageaccounts
    SET writable = _writable
    WHERE id = _id;

    RETURN true;
END;
$BODY$;
