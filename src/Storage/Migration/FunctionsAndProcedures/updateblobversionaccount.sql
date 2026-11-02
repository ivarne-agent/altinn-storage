-- Records that a blob version's blob is now read from _toaccountid. The row is changed only while it
-- still names _fromaccountid (NULL for the pattern default), so a concurrent re-point is not undone.
-- Returns the number of rows changed, 0 or 1.
CREATE OR REPLACE FUNCTION storage.updateblobversionaccount(_id UUID, _fromaccountid SMALLINT, _toaccountid SMALLINT)
    RETURNS INT
    LANGUAGE 'plpgsql'
AS $BODY$
DECLARE
    _updated INT;
BEGIN
    UPDATE storage.dataelementblobversions
    SET storageaccountid = _toaccountid
    WHERE id = _id
        AND storageaccountid IS NOT DISTINCT FROM _fromaccountid;

    GET DIAGNOSTICS _updated = ROW_COUNT;
    RETURN _updated;
END;
$BODY$;
