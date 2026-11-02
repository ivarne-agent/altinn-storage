CREATE OR REPLACE FUNCTION storage.readstorageaccount(_id SMALLINT)
    RETURNS TABLE (
        id SMALLINT,
        provider TEXT,
        providerinfo JSONB,
        org TEXT,
        storageaccountnumber INT,
        writable BOOLEAN,
        created TIMESTAMPTZ
    )
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
    RETURN QUERY
        SELECT
            sa.id,
            sa.provider,
            sa.providerinfo,
            sa.org,
            sa.storageaccountnumber,
            sa.writable,
            sa.created
        FROM storage.storageaccounts sa
        WHERE sa.id = _id;
END;
$BODY$;
