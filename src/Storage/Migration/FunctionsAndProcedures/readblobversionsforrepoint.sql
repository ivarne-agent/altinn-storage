-- Pages through the blob versions recorded on one storage account. With _fromaccountid NULL the rows
-- are those on the pattern default of (_org, _storageaccountnumber), where a NULL number means 1.
-- _afterid is exclusive; pass the nil UUID to start from the beginning. Paging on the primary key keeps
-- every page an ordered index scan.
CREATE OR REPLACE FUNCTION storage.readblobversionsforrepoint(
    _fromaccountid SMALLINT,
    _org TEXT,
    _storageaccountnumber INT,
    _appid TEXT,
    _afterid UUID,
    _limit INT
)
    RETURNS TABLE (
        id UUID,
        instanceguid UUID,
        dataelementid UUID,
        appid TEXT,
        blobstorageorg TEXT,
        storageaccountnumber INT,
        storageaccountid SMALLINT
    )
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
    RETURN QUERY
        SELECT
            bv.id,
            bv.instanceguid,
            bv.dataelementid,
            bv.appid,
            bv.blobstorageorg,
            bv.storageaccountnumber,
            bv.storageaccountid
        FROM storage.dataelementblobversions bv
        WHERE bv.id > _afterid
            AND bv.storageaccountid IS NOT DISTINCT FROM _fromaccountid
            AND (
                _fromaccountid IS NOT NULL
                OR (bv.blobstorageorg = _org AND COALESCE(bv.storageaccountnumber, 1) = _storageaccountnumber)
            )
            AND (_appid IS NULL OR bv.appid = _appid)
        ORDER BY bv.id
        LIMIT _limit;
END;
$BODY$;
